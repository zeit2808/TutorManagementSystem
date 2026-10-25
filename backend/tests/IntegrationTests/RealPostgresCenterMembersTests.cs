using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// Wave 2: owner-only member management, inviting an existing account, one active owner per account,
/// linking a teacher profile to a login, and teacher-scoped attendance until the end of the day.
/// </summary>
public sealed class RealPostgresCenterMembersTests
{
    [LocalPostgresFact]
    public async Task Owner_manages_members_and_linked_teacher_takes_own_attendance_the_same_day()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        // 15:00 in Vietnam today: sessions earlier today are still open for the teacher.
        var now = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero);
        var past = now.AddDays(-3);
        var owner = User.CreateExternal($"owner-{Guid.NewGuid():N}@example.invalid", past);
        var teacherUser = User.CreateExternal($"teacher-{Guid.NewGuid():N}@example.invalid", past);
        var stranger = User.CreateExternal($"stranger-{Guid.NewGuid():N}@example.invalid", past);
        // A Google student account: never invitable as staff.
        var studentUser = User.CreateExternal($"student-{Guid.NewGuid():N}@example.invalid", past);
        studentUser.AssignRole(SystemRoles.StudentId);
        var center = NewCenter("Members A", past, owner.Id);
        var otherCenter = NewCenter("Members B", past, owner.Id);
        var room = Room.Create(center.Id, "R1", "Phòng 1", null, owner.Id, past);

        await using var provider = BuildServices(connectionString, now);
        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                organization.Centers.AddRange(center, otherCenter);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                identity.Users.AddRange(owner, teacherUser, stranger, studentUser);
                identity.CenterMemberships.Add(CenterMembership.CreateOwner(center.Id, owner.Id, past));
                await identity.SaveChangesAsync();
            }

            // Only the owner invites; an existing account is invited, not duplicated.
            await using (var scope = provider.CreateAsyncScope())
            {
                var flow = scope.ServiceProvider.GetRequiredService<CenterMemberWorkflow>();
                var denied = await flow.InviteAsync(center.Id, teacherUser.Id, stranger.Email, "Teacher", default);
                Assert.Equal(MemberErrors.OwnerOnly, denied.Error);
                var badRole = await flow.InviteAsync(center.Id, owner.Id, teacherUser.Email, "CenterOwner", default);
                Assert.Equal(MemberErrors.InvalidRole, badRole.Error);
                var studentInvite = await flow.InviteAsync(center.Id, owner.Id, studentUser.Email, "Teacher", default);
                Assert.Equal(IdentityErrors.StudentAccountNotAllowed, studentInvite.Error);
                var invited = await flow.InviteAsync(center.Id, owner.Id, teacherUser.Email, "Teacher", default);
                Assert.True(invited.IsSuccess);
                Assert.True(invited.Value.AccountExists);
                Assert.Single(await flow.ListInvitationsAsync(center.Id, default));
            }

            var token = await ReadTokenAsync(provider, teacherUser.Email);
            await using (var scope = provider.CreateAsyncScope())
            {
                var flow = scope.ServiceProvider.GetRequiredService<CenterMemberWorkflow>();
                var preview = await flow.PreviewAsync(token, default);
                Assert.Equal("Teacher", preview.Value.Role);
                Assert.True(preview.Value.AccountExists);
                var wrongAccount = await flow.AcceptAsSignedInAsync(stranger.Id, token, default);
                Assert.Equal(IdentityErrors.InvitationEmailMismatch, wrongAccount.Error);
                var accepted = await flow.AcceptAsSignedInAsync(teacherUser.Id, token, default);
                Assert.True(accepted.IsSuccess);
                var reused = await flow.AcceptAsSignedInAsync(teacherUser.Id, token, default);
                Assert.Equal(IdentityErrors.InvalidCenterInvitation, reused.Error);
            }

            Guid teacherMembershipId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var flow = scope.ServiceProvider.GetRequiredService<CenterMemberWorkflow>();
                var roster = await flow.ListAsync(center.Id, default);
                Assert.Equal(2, roster.Count);
                var member = roster.Single(x => x.UserId == teacherUser.Id);
                Assert.Equal("Teacher", member.Role);
                teacherMembershipId = member.MembershipId;
                var ownerRow = roster.Single(x => x.UserId == owner.Id);
                Assert.Equal(MemberErrors.OwnerProtected,
                    (await flow.DeactivateAsync(center.Id, owner.Id, ownerRow.MembershipId, default)).Error);
                Assert.Equal("Staff", (await flow.ChangeRoleAsync(center.Id, owner.Id, teacherMembershipId, "Staff", default)).Value.Role);
                Assert.Equal("Teacher", (await flow.ChangeRoleAsync(center.Id, owner.Id, teacherMembershipId, "Teacher", default)).Value.Role);
            }

            // D2: an account owns at most one center, even through a valid owner invitation.
            await using (var scope = provider.CreateAsyncScope())
            {
                var tokens = scope.ServiceProvider.GetRequiredService<IOneTimeTokenService>();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                identity.CenterInvitations.Add(CenterInvitation.CreateOwner(otherCenter.Id, owner.Email,
                    owner.NormalizedEmail, tokens.Hash("second-owner-token"), now.AddHours(1), now, owner.Id));
                await identity.SaveChangesAsync();
                var flow = scope.ServiceProvider.GetRequiredService<CenterMemberWorkflow>();
                var second = await flow.AcceptAsSignedInAsync(owner.Id, "second-owner-token", default);
                Assert.Equal(IdentityErrors.AlreadyCenterOwner, second.Error);
            }

            // Teaching data: T1 linked to the invited account, T2 someone else.
            var subject = Subject.Create(center.Id, "MATH", "Toán", null, owner.Id, past);
            var group = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 10, owner.Id, past);
            var mine = Teacher.Create(center.Id, "GV1", "Giáo viên Một", past);
            var theirs = Teacher.Create(center.Id, "GV2", "Giáo viên Hai", past);
            var student = Student.Create(center.Id, "HS1", "Học sinh Một", past);
            var today = TeachingSession.Create(center.Id, group.Id, mine.Id, room.Id, now.AddMinutes(-60), now.AddMinutes(-15), past);
            var colleague = TeachingSession.Create(center.Id, group.Id, theirs.Id, room.Id, now.AddHours(-3), now.AddHours(-2), past);
            var yesterday = TeachingSession.Create(center.Id, group.Id, mine.Id, room.Id, now.AddDays(-1), now.AddDays(-1).AddHours(1), past);
            group.Enroll(past);
            await using (var scope = provider.CreateAsyncScope())
            {
                var academic = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
                academic.Subjects.Add(subject);
                academic.Classes.Add(group);
                academic.Teachers.AddRange(mine, theirs);
                academic.Students.Add(student);
                academic.Enrollments.Add(Enrollment.Create(center.Id, group.Id, student.Id, past));
                academic.Sessions.AddRange(today, colleague, yesterday);
                await academic.SaveChangesAsync();
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var accounts = scope.ServiceProvider.GetRequiredService<TeacherAccountWorkflow>();
                Assert.Equal(TeachingErrors.AccountNotMember,
                    (await accounts.LinkAsync(center.Id, theirs.Id, stranger.Id, 0, default)).Error);
                var linked = await accounts.LinkAsync(center.Id, mine.Id, teacherUser.Id, 0, default);
                Assert.True(linked.IsSuccess);
                Assert.Equal(teacherUser.Id, linked.Value.UserId);
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var accounts = scope.ServiceProvider.GetRequiredService<TeacherAccountWorkflow>();
                Assert.Equal(TeachingErrors.AccountInUse,
                    (await accounts.LinkAsync(center.Id, theirs.Id, teacherUser.Id, 0, default)).Error);
                var schedule = await accounts.MyTeachingAsync(center.Id, teacherUser.Id, now.AddDays(-2), now.AddDays(1), default);
                Assert.Equal(mine.Id, schedule.Value.Teacher!.Id);
                Assert.Equal(new[] { yesterday.Id, today.Id }, schedule.Value.Sessions.Select(x => x.Id).ToArray());
                Assert.All(schedule.Value.Sessions, x => Assert.Equal("Toán 9A", x.ClassName));
            }

            // D3: the linked teacher takes attendance for their own session today, nothing else.
            var actor = new AttendanceActor(teacherUser.Id, CanManage: false);
            await using (var scope = provider.CreateAsyncScope())
            {
                var attendance = scope.ServiceProvider.GetRequiredService<AttendanceWorkflow>();
                Assert.True((await attendance.GetAsync(center.Id, today.Id, actor, default)).IsSuccess);
                var saved = await attendance.SaveDraftAsync(center.Id, today.Id,
                    new SaveAttendanceDraftRequest(0, mine.Id, null, "Present", []), actor, default);
                Assert.True(saved.IsSuccess);
                var submitted = await attendance.SubmitAsync(center.Id, today.Id, saved.Value.Version, actor, default);
                Assert.True(submitted.IsSuccess);
                Assert.Equal(TeachingErrors.AttendanceNotAssigned,
                    (await attendance.GetAsync(center.Id, colleague.Id, actor, default)).Error);
                Assert.True((await attendance.GetAsync(center.Id, yesterday.Id, actor, default)).IsSuccess);
                Assert.Equal(TeachingErrors.AttendanceWindowClosed, (await attendance.SaveDraftAsync(center.Id,
                    yesterday.Id, new SaveAttendanceDraftRequest(0, mine.Id, null, "Present", []), actor, default)).Error);
            }

            // Deactivation takes effect for other modules immediately.
            await using (var scope = provider.CreateAsyncScope())
            {
                var flow = scope.ServiceProvider.GetRequiredService<CenterMemberWorkflow>();
                Assert.False((await flow.DeactivateAsync(center.Id, owner.Id, teacherMembershipId, default)).Value.IsActive);
                var directory = scope.ServiceProvider.GetRequiredService<ICenterMemberDirectory>();
                Assert.Null(await directory.FindActiveAsync(center.Id, teacherUser.Id, default));
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM academics.attendance_revisions WHERE center_id = @center;
                DELETE FROM academics.attendance_entries WHERE center_id = @center;
                DELETE FROM academics.attendance_sheets WHERE center_id = @center;
                DELETE FROM academics.teaching_sessions WHERE center_id = @center;
                DELETE FROM academics.enrollments WHERE center_id = @center;
                DELETE FROM academics.students WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM iam.center_invitations WHERE center_id IN (@center, @other);
                DELETE FROM iam.center_memberships WHERE center_id IN (@center, @other);
                DELETE FROM iam.email_outbox_messages WHERE recipient IN (@ownerEmail, @teacherEmail, @strangerEmail);
                DELETE FROM organization.centers WHERE id IN (@center, @other);
                DELETE FROM iam.user_roles WHERE user_id = @studentUser;
                DELETE FROM iam.users WHERE id IN (@owner, @teacher, @stranger, @studentUser);
                """, connection);
            cleanup.Parameters.AddWithValue("center", center.Id);
            cleanup.Parameters.AddWithValue("other", otherCenter.Id);
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("teacher", teacherUser.Id);
            cleanup.Parameters.AddWithValue("stranger", stranger.Id);
            cleanup.Parameters.AddWithValue("studentUser", studentUser.Id);
            cleanup.Parameters.AddWithValue("ownerEmail", owner.Email);
            cleanup.Parameters.AddWithValue("teacherEmail", teacherUser.Email);
            cleanup.Parameters.AddWithValue("strangerEmail", stranger.Email);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static Center NewCenter(string name, DateTimeOffset now, Guid reviewer)
    {
        var center = Center.Create(name, name,
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"center-{Guid.NewGuid():N}@example.invalid", "0900000000", now);
        center.Approve(reviewer, now);
        return center;
    }

    private static ServiceProvider BuildServices(string connectionString, DateTimeOffset now)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedInfrastructure();
        services.AddIdentityAccessInfrastructure(connectionString);
        services.AddOrganizationsInfrastructure(connectionString);
        services.AddAcademicsInfrastructure(connectionString);
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FixedClock(now));
        return services.BuildServiceProvider();
    }

    private static async Task<string> ReadTokenAsync(ServiceProvider provider, string email)
    {
        await using var scope = provider.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var body = await identity.EmailOutboxMessages.Where(x => x.Recipient == email)
            .OrderByDescending(x => x.CreatedAtUtc).Select(x => x.Body).FirstAsync();
        var lines = body.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines[Array.FindIndex(lines, x => x.EndsWith("Mã lời mời:", StringComparison.Ordinal)) + 1];
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
