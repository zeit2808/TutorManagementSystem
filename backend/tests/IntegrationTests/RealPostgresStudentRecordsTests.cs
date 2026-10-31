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
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// Wave 3: parent contact (D5), withdrawal reasons (D7), HS-000001 codes (D9), submit/correct-only
/// attendance history (D11), actor names (C2) and server-side search and filters (C3).
/// </summary>
public sealed class RealPostgresStudentRecordsTests
{
    [LocalPostgresFact]
    public async Task Student_records_codes_withdrawals_history_and_search_follow_wave3_rules()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero);
        var past = now.AddDays(-3);
        var owner = User.CreateExternal($"owner3-{Guid.NewGuid():N}@example.invalid", past);
        var learner = User.CreateExternal($"learner3-{Guid.NewGuid():N}@example.invalid", past);
        var center = Center.Create("Wave 3", "Wave 3",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"wave3-{Guid.NewGuid():N}@example.invalid", "0900000000", past);
        center.Approve(owner.Id, past);
        var room = Room.Create(center.Id, "R1", "Phòng 1", null, owner.Id, past);
        await using var provider = BuildServices(connectionString, now);

        try
        {
            await using (var scope = provider.CreateAsyncScope())
            {
                var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
                organization.Centers.Add(center);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
                var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                identity.Users.AddRange(owner, learner);
                await identity.SaveChangesAsync();
            }

            // D5 + D9: parent contact is required; codes are always generated (HS-000001…), and a code an older
            // record already holds (typed by hand before D9 covered every create) is skipped.
            Guid firstId, secondId;
            await using (var scope = provider.CreateAsyncScope())
            {
                var people = scope.ServiceProvider.GetRequiredService<PeopleCatalog>();
                Assert.Equal(TeachingErrors.InvalidParentPhone,
                    (await people.CreateStudentAsync(center.Id, "Lê Văn A", "Phụ huynh A", "12345", default)).Error);
                var first = await people.CreateStudentAsync(center.Id, "Lê Văn An", "Lê Văn Ba", "0912 345 678", default);
                Assert.Equal("HS-000001", first.Value.Code);
                Assert.Equal("+84912345678", first.Value.ParentPhone);
                var academics = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
                var legacy = Student.Create(center.Id, "HS-000002", "Trần Thị Bình", past);
                legacy.UpdateProfile("Trần Thị Bình", "Trần Văn Ca", "0987654321");
                await academics.Students.AddAsync(legacy);
                await academics.SaveChangesAsync();
                var third = await people.CreateStudentAsync(center.Id, "Phạm Minh Châu", "Phạm Văn Dũng", "+84 33 123 4567", default);
                Assert.Equal("HS-000003", third.Value.Code);
                firstId = first.Value.Id;
                secondId = third.Value.Id;

                // C3: search by name, code and parent phone on the server.
                Assert.Equal("Lê Văn An", Assert.Single((await people.ListStudentsAsync(center.Id, false, "văn an", 1, 25, default)).Items).FullName);
                Assert.Single((await people.ListStudentsAsync(center.Id, false, "HS-000003", 1, 25, default)).Items);
                Assert.Single((await people.ListStudentsAsync(center.Id, false, "987654", 1, 25, default)).Items);
                Assert.Empty((await people.ListStudentsAsync(center.Id, false, "100%", 1, 25, default)).Items);
            }

            var subject = Subject.Create(center.Id, "MATH", "Toán", null, owner.Id, past);
            var group = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 10, owner.Id, past);
            var other = TeachingClass.Create(center.Id, subject.Id, "L9B", "Lý 9B", 10, owner.Id, past);
            other.SetRegistrationOpen(true, past);
            var teacher = Teacher.Create(center.Id, "GV1", "Nguyễn Thầy", past);
            var session = TeachingSession.Create(center.Id, group.Id, teacher.Id, room.Id, now.AddMinutes(-60), now.AddMinutes(-15), past);
            await using (var scope = provider.CreateAsyncScope())
            {
                var academic = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
                academic.Subjects.Add(subject);
                academic.Classes.AddRange(group, other);
                academic.Teachers.Add(teacher);
                await academic.SaveChangesAsync();
            }
            await using (var scope = provider.CreateAsyncScope())
            {
                var enrollments = scope.ServiceProvider.GetRequiredService<EnrollmentWorkflow>();
                Assert.True((await enrollments.EnrollAsync(center.Id, group.Id, firstId, default)).IsSuccess);
                Assert.True((await enrollments.EnrollAsync(center.Id, group.Id, secondId, default)).IsSuccess);
                var classes = scope.ServiceProvider.GetRequiredService<ClassCatalog>();
                Assert.Equal("L9B", Assert.Single((await classes.ListAsync(center.Id, false, "lý", 1, 25, default)).Items).Code);
            }
            // The session was created before enrollment in real life; insert it now and backdate enrollment.
            await using (var scope = provider.CreateAsyncScope())
            {
                var academic = scope.ServiceProvider.GetRequiredService<AcademicsDbContext>();
                academic.Sessions.Add(session);
                await academic.SaveChangesAsync();
                await academic.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE academics.enrollments SET enrolled_at_utc = {past} WHERE center_id = {center.Id}");
            }

            // D11 + C2: drafts leave no history rows; submissions carry the actor's name.
            await using (var scope = provider.CreateAsyncScope())
            {
                var attendance = scope.ServiceProvider.GetRequiredService<AttendanceWorkflow>();
                var draft = await attendance.SaveDraftAsync(center.Id, session.Id,
                    new SaveAttendanceDraftRequest(0, teacher.Id, null, "Present", []), owner.Id, default);
                var draft2 = await attendance.SaveDraftAsync(center.Id, session.Id,
                    new SaveAttendanceDraftRequest(draft.Value.Version, teacher.Id, "Ghi chú", "Present", []), owner.Id, default);
                Assert.Empty((await attendance.HistoryAsync(center.Id, session.Id, default)).Value);
                Assert.True((await attendance.SubmitAsync(center.Id, session.Id, draft2.Value.Version, owner.Id, default)).IsSuccess);
                var history = (await attendance.HistoryAsync(center.Id, session.Id, default)).Value;
                var submitted = Assert.Single(history);
                Assert.Equal("Submitted", submitted.Action);
                Assert.Equal(owner.Email, submitted.ActorName);
            }

            // D7: a reason is required, withdrawal is immediate, and the roster shows who and why.
            await using (var scope = provider.CreateAsyncScope())
            {
                var enrollments = scope.ServiceProvider.GetRequiredService<EnrollmentWorkflow>();
                Assert.Equal(TeachingErrors.InvalidWithdrawalReason,
                    (await enrollments.WithdrawAsync(center.Id, group.Id, firstId, "  ", owner.Id, default)).Error);
                var withdrawn = await enrollments.WithdrawAsync(center.Id, group.Id, firstId, "Gia đình chuyển nhà", owner.Id, default);
                Assert.Equal(now, withdrawn.Value.WithdrawnAtUtc);
                var row = Assert.Single((await enrollments.ListAsync(center.Id, group.Id, EnrollmentFilter.Withdrawn, 1, 25, default)).Value.Items);
                Assert.Equal("Gia đình chuyển nhà", row.WithdrawalReason);
                Assert.Equal(owner.Email, row.WithdrawnByName);
            }

            // D5 + D9 for online students: the application carries parent contact into the new HS code record.
            await using (var scope = provider.CreateAsyncScope())
            {
                var registration = scope.ServiceProvider.GetRequiredService<ClassRegistrationWorkflow>();
                Assert.Equal(ClassRegistrationErrors.InvalidParentPhone, (await registration.SubmitAsync(center.Id, learner.Id,
                    "Học Sinh Mạng", "Phụ huynh", "abc", [other.Id], Guid.NewGuid().ToString(), default)).Error);
                var submitted = await registration.SubmitAsync(center.Id, learner.Id, "Học Sinh Mạng", "Mẹ Học Sinh",
                    "0901112233", [other.Id], Guid.NewGuid().ToString(), default);
                var approved = await registration.ApproveAsync(center.Id, submitted.Value.Items[0].Id, owner.Id, default);
                Assert.True(approved.IsSuccess);
                var listed = Assert.Single((await registration.ListApplicationsAsync(center.Id, null, other.Id, 1, 25, default)).Items);
                Assert.Equal("+84901112233", listed.ParentPhone);
                Assert.Equal(owner.Email, listed.ReviewedByName);
                Assert.Empty((await registration.ListApplicationsAsync(center.Id, null, group.Id, 1, 25, default)).Items);
                var people = scope.ServiceProvider.GetRequiredService<PeopleCatalog>();
                var online = Assert.Single((await people.ListStudentsAsync(center.Id, false, "mạng", 1, 25, default)).Items);
                Assert.Equal("HS-000004", online.Code);
                Assert.Equal("Mẹ Học Sinh", online.ParentName);
            }

            // C3: sessions can be narrowed by teacher, class or room.
            await using (var scope = provider.CreateAsyncScope())
            {
                var scheduler = scope.ServiceProvider.GetRequiredService<SessionScheduler>();
                Assert.Single((await scheduler.ListAsync(center.Id, now.AddDays(-1), now.AddDays(1),
                    new SessionFilter(TeacherId: teacher.Id), default)).Value);
                Assert.Empty((await scheduler.ListAsync(center.Id, now.AddDays(-1), now.AddDays(1),
                    new SessionFilter(ClassId: other.Id), default)).Value);
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
                DELETE FROM academics.class_applications WHERE center_id = @center;
                DELETE FROM academics.students WHERE center_id = @center;
                DELETE FROM academics.code_counters WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id = @center;
                DELETE FROM iam.email_outbox_messages WHERE recipient IN (SELECT email FROM iam.users WHERE id IN (@owner, @learner));
                DELETE FROM iam.users WHERE id IN (@owner, @learner);
                """, connection);
            cleanup.Parameters.AddWithValue("center", center.Id);
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("learner", learner.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
