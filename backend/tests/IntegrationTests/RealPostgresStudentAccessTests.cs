using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Codes;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// M3 against real PostgreSQL: personal activation links for hand-made profiles, class invite links with a code
/// (pre-approved), the duplicate signal when one login meets two profiles, merging, and notices when the center
/// enrolls a student who has a login.
/// </summary>
public sealed class RealPostgresStudentAccessTests
{
    [LocalPostgresFact]
    public async Task Activation_links_class_invites_and_merge_keep_one_profile_per_student()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = DateTimeOffset.UtcNow;
        var t0 = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var past = t0.AddDays(-30);
        var clock = new FixedClock(t0);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var owner = User.CreateExternal($"owner-{marker}@example.invalid", past);
        var nam = User.CreateExternal($"nam-{marker}@example.invalid", past);
        var hoa = User.CreateExternal($"hoa-{marker}@example.invalid", past);
        var stranger = User.CreateExternal($"stranger-{marker}@example.invalid", past);
        var center = Center.Create("Access LLC", $"Access {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"access-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var options = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(owner, nam, hoa, stranger);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.Add(center);
                await organization.SaveChangesAsync();
            }
            var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, owner.Id, past);
            var t9a = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 20, owner.Id, past);
            var t9b = TeachingClass.Create(center.Id, subject.Id, "T9B", "Toán 9B", 20, owner.Id, past);
            var tiny = TeachingClass.Create(center.Id, subject.Id, "T1", "Lớp một chỗ", 1, owner.Id, past);
            // Hand-made profiles: Nam studies T9A; "Phạm Hoa" exists without a login (namesake hint).
            var namProfile = Student.Create(center.Id, "HS-000001", "Trần Văn Nam", past);
            var hoaManual = Student.Create(center.Id, "HS-000002", "Phạm Hoa", past);
            var enrollNam = Enrollment.Create(center.Id, t9a.Id, namProfile.Id, past);
            t9a.Enroll(past);
            await using (var db = new AcademicsDbContext(options))
            {
                db.Subjects.Add(subject);
                db.Classes.AddRange(t9a, t9b, tiny);
                db.Students.AddRange(namProfile, hoaManual);
                db.Enrollments.Add(enrollNam);
                await db.SaveChangesAsync();
            }

            StudentAccessWorkflow Access(AcademicsDbContext db) => new(new StudentLinkStore(db), new TeachingStore(db), clock);
            StudentJoinWorkflow Join(AcademicsDbContext db) => new(new StudentLinkStore(db), new TeachingStore(db),
                new AcademicsCodeGenerator(db), new StudentTimetableStore(db), new CourseDirectoryStore(db), clock,
                new ScheduleConflictChecker(new StudentTimetableStore(db), clock));

            // ---- Class invite: wrong codes lock, the right code joins at once and makes a profile ----
            ClassInviteDto invite;
            await using (var db = new AcademicsDbContext(options))
            {
                Assert.Equal(StudentLinkErrors.InvalidInvite,
                    (await Access(db).CreateInviteAsync(center.Id, t9b.Id, 3, null, owner.Id, default)).Error);
                invite = (await Access(db).CreateInviteAsync(center.Id, t9b.Id, 7, 10, owner.Id, default)).Value;
                Assert.Equal(6, invite.Code.Length);
                Assert.Equal(t0.AddDays(7), invite.ExpiresAtUtc);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var preview = (await Join(db).PreviewInviteAsync(invite.Token, hoa.Id, default)).Value;
                Assert.Equal("Toán 9B", preview.ClassName);
                Assert.False(preview.HasProfileHere);
            }
            for (var i = 1; i <= 5; i++)
            {
                await using var db = new AcademicsDbContext(options);
                var (wrong, _) = await Join(db).JoinAsync(invite.Token, stranger.Id, "ZZZZZZ", "Người Lạ", "Phụ Huynh",
                    "0912000099", false, false, default);
                Assert.Equal(i < 5 ? StudentLinkErrors.WrongCode : StudentLinkErrors.InviteLocked, wrong.Error);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var (locked, _) = await Join(db).JoinAsync(invite.Token, stranger.Id, invite.Code, "Người Lạ", "Phụ Huynh",
                    "0912000099", false, false, default);
                Assert.Equal(StudentLinkErrors.InviteLocked, locked.Error);
                // "Đã từng học ở đây" without a linked profile is sent to the activation link.
                var (before, _) = await Join(db).JoinAsync(invite.Token, hoa.Id, invite.Code.ToLowerInvariant(), "Phạm Hoa",
                    "Phạm Bố", "0912000002", true, false, default);
                Assert.Equal(StudentLinkErrors.UseActivationLink, before.Error);
            }
            Guid hoaNewId;
            await using (var db = new AcademicsDbContext(options))
            {
                var (joined, _) = await Join(db).JoinAsync(invite.Token, hoa.Id, invite.Code.ToLowerInvariant(), "Phạm Hoa",
                    "Phạm Bố", "0912000002", false, false, default);
                Assert.True(joined.IsSuccess, joined.Error.Code);
                Assert.True(joined.Value.CreatedProfile);
                hoaNewId = joined.Value.StudentId;
                var (again, _) = await Join(db).JoinAsync(invite.Token, hoa.Id, invite.Code, null, null, null, false,
                    false, default);
                Assert.Equal(ClassRegistrationErrors.AlreadyEnrolled, again.Error);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var created = await db.Students.SingleAsync(x => x.Id == hoaNewId);
                Assert.Equal(hoa.Id, created.UserId);
                Assert.Equal("+84912000002", created.ParentPhone);
                // Same name as the hand-made profile: a hint for the center, nothing merged.
                Assert.Equal(hoaManual.Id, created.DuplicateCandidateId);
                Assert.Equal(StudentDuplicateReason.SameName, created.DuplicateReason);
                Assert.Equal(1, (await db.ClassInviteLinks.SingleAsync(x => x.Id == invite.Id)).UseCount);
                Assert.Single(await db.ClassInviteJoins.Where(x => x.LinkId == invite.Id).ToListAsync());
                Assert.Equal(1, (await db.Classes.SingleAsync(x => x.Id == t9b.Id)).EnrolledCount);
            }

            // ---- Nam does it in the wrong order: joins T9B by the class link first, then opens his activation link ----
            ActivationLinkDto namLink;
            await using (var db = new AcademicsDbContext(options))
            {
                namLink = (await Access(db).CreateActivationLinkAsync(center.Id, namProfile.Id, owner.Id, default)).Value;
                var (joined, _) = await Join(db).JoinAsync(invite.Token, nam.Id, invite.Code, "Trần Văn Nam", "Trần Bố",
                    "0912000001", false, false, default);
                Assert.True(joined.IsSuccess, joined.Error.Code);
            }
            Guid namDuplicateId;
            await using (var db = new AcademicsDbContext(options))
            {
                var preview = (await Join(db).PreviewActivationAsync(namLink.Token, nam.Id, default)).Value;
                Assert.Equal("Trần Văn Nam", preview.FullName);
                Assert.Equal(["Toán 9A"], preview.ClassNames);
                var refused = await Join(db).AcceptActivationAsync(namLink.Token, nam.Id, default);
                Assert.Equal(StudentLinkErrors.AccountHasProfile, refused.Error);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var manual = await db.Students.SingleAsync(x => x.Id == namProfile.Id);
                var duplicate = await db.Students.SingleAsync(x => x.CenterId == center.Id && x.UserId == nam.Id);
                namDuplicateId = duplicate.Id;
                Assert.Null(manual.UserId);
                Assert.Equal(duplicate.Id, manual.DuplicateCandidateId);
                Assert.Equal(StudentDuplicateReason.SameAccount, manual.DuplicateReason);
                Assert.Equal(manual.Id, duplicate.DuplicateCandidateId);
            }
            // The center merges the class-link profile into the hand-made one: one profile with both classes.
            await using (var db = new AcademicsDbContext(options))
            {
                var merged = await Access(db).MergeAsync(center.Id, namProfile.Id, namDuplicateId, default);
                Assert.True(merged.IsSuccess, merged.Error.Code);
                Assert.Equal(1, merged.Value.MovedEnrollments);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var target = await db.Students.SingleAsync(x => x.Id == namProfile.Id);
                var source = await db.Students.SingleAsync(x => x.Id == namDuplicateId);
                Assert.Equal(nam.Id, target.UserId);
                Assert.Null(target.DuplicateCandidateId);
                Assert.Null(source.UserId);
                Assert.Equal(target.Id, source.MergedIntoStudentId);
                Assert.False(source.IsActive);
                var classes = await db.Enrollments.Where(x => x.StudentId == target.Id && x.WithdrawnAtUtc == null)
                    .Select(x => x.ClassId).ToListAsync();
                Assert.Equal(new[] { t9a.Id, t9b.Id }.Order(), classes.Order());
                Assert.Equal(1, await db.ClassInviteJoins.CountAsync(x => x.StudentId == target.Id));
                // The old activation link no longer works once the profile has a login.
                Assert.Equal(StudentLinkErrors.ActivationInvalid,
                    (await Join(db).PreviewActivationAsync(namLink.Token, stranger.Id, default)).Error);
            }

            // ---- Merge guard: two profiles in the same class are two seats, not one student ----
            await using (var db = new AcademicsDbContext(options))
            {
                Assert.Equal(StudentLinkErrors.MergeTwoAccounts,
                    (await Access(db).MergeAsync(center.Id, namProfile.Id, hoaNewId, default)).Error);
                var enrollments = new EnrollmentWorkflow(new TeachingStore(db), clock);
                Assert.True((await enrollments.EnrollAsync(center.Id, t9b.Id, hoaManual.Id, default)).IsSuccess);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                Assert.Equal(StudentLinkErrors.MergeSharedClass,
                    (await Access(db).MergeAsync(center.Id, hoaManual.Id, hoaNewId, default)).Error);
            }

            // ---- Activation in the right order, then "Không phải tôi" on another link ----
            var hoaOther = Student.Create(center.Id, "HS-000009", "Lê Thu", past);
            await using (var db = new AcademicsDbContext(options))
            {
                db.Students.Add(hoaOther);
                await db.SaveChangesAsync();
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var link = (await Access(db).CreateActivationLinkAsync(center.Id, hoaOther.Id, owner.Id, default)).Value;
                Assert.True((await Join(db).DeclineActivationAsync(link.Token, default)).IsSuccess);
                Assert.Equal(StudentLinkErrors.ActivationInvalid,
                    (await Join(db).AcceptActivationAsync(link.Token, stranger.Id, default)).Error);
                var fresh = (await Access(db).CreateActivationLinkAsync(center.Id, hoaOther.Id, owner.Id, default)).Value;
                var accepted = await Join(db).AcceptActivationAsync(fresh.Token, stranger.Id, default);
                Assert.True(accepted.IsSuccess, accepted.Error.Code);
                // Opening it again is harmless for its owner and refused for anyone else.
                Assert.True((await Join(db).AcceptActivationAsync(fresh.Token, stranger.Id, default)).IsSuccess);
                Assert.Equal(StudentLinkErrors.ActivationInvalid,
                    (await Join(db).AcceptActivationAsync(fresh.Token, hoa.Id, default)).Error);
            }

            // ---- The center enrolls a student with a login: allowed, and only that student is told ----
            await using (var db = new AcademicsDbContext(options))
            {
                var recorder = new ScheduleChangeRecorder(
                    new ScheduleChangeStore(db, clock, new ServiceCollection().BuildServiceProvider()),
                    new StudentTimetableStore(db), new ScheduleConflictChecker(new StudentTimetableStore(db), clock));
                var enrollments = new EnrollmentWorkflow(new TeachingStore(db), clock, null,
                    new ScheduleConflictChecker(new StudentTimetableStore(db), clock), recorder);
                var (enrolled, _) = await enrollments.EnrollAsync(center.Id, tiny.Id, hoaOther.Id, false, owner.Id, default);
                Assert.True(enrolled.IsSuccess, enrolled.Error.Code);
            }
            await using (var db = new AcademicsDbContext(options))
            {
                var notice = await db.ScheduleNotices.SingleAsync(x => x.CenterId == center.Id && x.StudentId == hoaOther.Id);
                Assert.Equal(stranger.Id, notice.UserId);
                Assert.True(notice.EmailQueued);
                var change = await db.ScheduleChanges.SingleAsync(x => x.Id == notice.ChangeId);
                Assert.Equal(ScheduleChangeKind.StudentEnrolled, change.Kind);
                // The center's change log does not list enrollments.
                var queries = new ScheduleChangeStore(db, clock, new ServiceCollection().BuildServiceProvider());
                Assert.Empty((await queries.ListCenterChangesAsync(center.Id, null, 1, 20, default)).Items);
            }

            // ---- A full class: the right code does not create a profile or use up the link ----
            await using (var db = new AcademicsDbContext(options))
            {
                var tinyInvite = (await Access(db).CreateInviteAsync(center.Id, tiny.Id, 1, null, owner.Id, default)).Value;
                var (full, _) = await Join(db).JoinAsync(tinyInvite.Token, nam.Id, tinyInvite.Code, null, null, null,
                    false, false, default);
                Assert.Equal(ClassRegistrationErrors.Full, full.Error);
                var revoked = (await Access(db).RevokeInviteAsync(center.Id, tinyInvite.Id, default)).Value;
                Assert.False(revoked.IsActive);
                Assert.Equal(StudentLinkErrors.InviteInvalid,
                    (await Join(db).PreviewInviteAsync(tinyInvite.Token, nam.Id, default)).Error);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM academics.class_invite_joins WHERE center_id = @c;
                DELETE FROM academics.class_invite_attempts WHERE link_id IN (SELECT id FROM academics.class_invite_links WHERE center_id = @c);
                DELETE FROM academics.class_invite_links WHERE center_id = @c;
                DELETE FROM academics.student_activation_links WHERE center_id = @c;
                DELETE FROM academics.schedule_notices WHERE center_id = @c;
                DELETE FROM academics.schedule_changes WHERE center_id = @c;
                DELETE FROM academics.enrollments WHERE center_id = @c;
                DELETE FROM academics.classes WHERE center_id = @c;
                DELETE FROM academics.students WHERE center_id = @c;
                DELETE FROM academics.code_counters WHERE center_id = @c;
                DELETE FROM academics.subjects WHERE center_id = @c;
                DELETE FROM organization.center_status_history WHERE center_id = @c;
                DELETE FROM organization.centers WHERE id = @c;
                DELETE FROM iam.email_outbox_messages WHERE recipient LIKE @pattern;
                DELETE FROM iam.users WHERE id IN (@owner, @nam, @hoa, @stranger);
                """, connection);
            cleanup.Parameters.AddWithValue("c", center.Id);
            cleanup.Parameters.AddWithValue("pattern", $"%-{marker}@example.invalid");
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("nam", nam.Id);
            cleanup.Parameters.AddWithValue("hoa", hoa.Id);
            cleanup.Parameters.AddWithValue("stranger", stranger.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
