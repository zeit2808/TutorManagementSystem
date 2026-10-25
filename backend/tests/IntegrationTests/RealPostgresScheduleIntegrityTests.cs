using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// Wave 1 fixes: the student schedule follows the enrollment window, the database rejects overlapping
/// sessions, the registration toggle is version-checked, archived teachers cannot be picked as
/// substitutes, and read models carry display names.
/// </summary>
public sealed class RealPostgresScheduleIntegrityTests
{
    [LocalPostgresFact]
    public async Task Schedule_reads_and_writes_respect_enrollment_window_versions_and_overlaps()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var t0 = new DateTimeOffset(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month,
            DateTimeOffset.UtcNow.Day, DateTimeOffset.UtcNow.Hour, 0, 0, TimeSpan.Zero);
        var past = t0.AddDays(-30);
        var clock = new FixedClock(t0);
        var actor = User.CreateExternal($"owner-{Guid.NewGuid():N}@example.invalid", past);
        var learner = User.CreateExternal($"learner-{Guid.NewGuid():N}@example.invalid", past);
        var center = Center.Create("Schedule integrity", "Schedule integrity",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"schedule-{Guid.NewGuid():N}@example.invalid", "0900000000", past);
        center.Approve(actor.Id, past);
        var room = Room.Create(center.Id, "R1", "Phòng 101", null, actor.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(actor, learner);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.Add(center);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
            }

            var subject = Subject.Create(center.Id, "MATH", "Toán", null, actor.Id, past);
            var group = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 10, actor.Id, past);
            var other = TeachingClass.Create(center.Id, subject.Id, "T9B", "Toán 9B", 10, actor.Id, past);
            var teacher = Teacher.Create(center.Id, "GV1", "Nguyễn Văn A", past);
            var substitute = Teacher.Create(center.Id, "GV2", "Trần Thị B", past);
            var student = Student.CreateForUser(center.Id, learner.Id, "HS-1", "Lê Văn C", past);
            TeachingSession PastSession(int dayOffset) => TeachingSession.Create(center.Id, group.Id,
                teacher.Id, room.Id, t0.AddDays(dayOffset), t0.AddDays(dayOffset).AddHours(1), past);
            var beforeJoin = PastSession(-20);
            var whileEnrolled = PastSession(-10);
            var lastBeforeLeaving = PastSession(-2);
            var afterLeaving = TeachingSession.Create(center.Id, group.Id, teacher.Id, room.Id,
                t0.AddDays(5), t0.AddDays(5).AddHours(1), t0);

            group.Enroll(t0.AddDays(-15));
            var enrollment = Enrollment.Create(center.Id, group.Id, student.Id, t0.AddDays(-15));
            await using (var academic = new AcademicsDbContext(academicOptions))
            {
                academic.Subjects.Add(subject);
                academic.Classes.AddRange(group, other);
                academic.Teachers.AddRange(teacher, substitute);
                academic.Students.Add(student);
                academic.Sessions.AddRange(beforeJoin, whileEnrolled, lastBeforeLeaving, afterLeaving);
                academic.Enrollments.Add(enrollment);
                await academic.SaveChangesAsync();
            }
            await using (var academic = new AcademicsDbContext(academicOptions))
            {
                var tracked = await academic.Enrollments.SingleAsync(x => x.Id == enrollment.Id);
                var trackedClass = await academic.Classes.SingleAsync(x => x.Id == group.Id);
                tracked.Withdraw(t0.AddDays(-1), "Moved to another center", actor.Id);
                trackedClass.Withdraw(t0.AddDays(-1));
                await academic.SaveChangesAsync();
            }

            // A1 + C1: only sessions inside [enrolled, withdrawn), with display names.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db),
                    clock, new TeachingNameReader(db));
                var sessions = await flow.MySessionsAsync(center.Id, learner.Id, t0.AddDays(-30), t0.AddDays(30), default);
                Assert.Equal(new[] { whileEnrolled.Id, lastBeforeLeaving.Id }, sessions.Select(x => x.Id).ToArray());
                Assert.All(sessions, x =>
                {
                    Assert.Equal("T9A", x.ClassCode);
                    Assert.Equal("Toán 9A", x.ClassName);
                    Assert.Equal("Nguyễn Văn A", x.TeacherName);
                    Assert.Equal("Phòng 101", x.RoomName);
                });
            }

            // A2: the database refuses an overlapping teacher booking even when application checks are bypassed.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var store = new TeachingStore(db);
                await store.AddAsync(TeachingSession.Create(center.Id, other.Id, teacher.Id, room.Id,
                    afterLeaving.StartUtc.AddMinutes(30), afterLeaving.EndUtc.AddMinutes(30), t0), default);
                var error = await Assert.ThrowsAsync<TeachingPersistenceException>(() => store.SaveAsync(default));
                Assert.True(error.IsScheduleConflict);
                Assert.StartsWith("ex_sessions_", error.ConstraintName, StringComparison.Ordinal);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                // Back-to-back sessions are not an overlap.
                var adjacent = TeachingSession.Create(center.Id, other.Id, teacher.Id, room.Id,
                    afterLeaving.EndUtc, afterLeaving.EndUtc.AddHours(1), t0);
                Assert.True(await new TeachingStore(db).TryAddSessionAsync(adjacent, default));
            }

            // A3: opening registration requires the version the client last saw.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db), clock);
                var stale = await flow.SetRegistrationAsync(center.Id, other.Id, true, other.Version + 1, default);
                Assert.Equal(ClassRegistrationErrors.ConcurrentChange, stale.Error);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db), clock);
                var opened = await flow.SetRegistrationAsync(center.Id, other.Id, true, other.Version, default);
                Assert.True(opened.IsSuccess);
                Assert.True(opened.Value.RegistrationOpen);
                Assert.Equal(other.Version + 1, opened.Value.Version);
            }

            // A4: an archived teacher cannot be chosen as the substitute.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var tracked = await db.Teachers.SingleAsync(x => x.Id == substitute.Id);
                tracked.Archive(t0);
                await db.SaveChangesAsync();
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var store = new TeachingStore(db);
                var attendance = new AttendanceWorkflow(store, new AttendanceStore(db), clock, new TeachingNameReader(db));
                var rejected = await attendance.SaveDraftAsync(center.Id, lastBeforeLeaving.Id,
                    new SaveAttendanceDraftRequest(0, substitute.Id, null, "Present", []), actor.Id, default);
                Assert.Equal(TeachingErrors.TeacherUnavailable, rejected.Error);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var store = new TeachingStore(db);
                var attendance = new AttendanceWorkflow(store, new AttendanceStore(db), clock, new TeachingNameReader(db));
                var saved = await attendance.SaveDraftAsync(center.Id, lastBeforeLeaving.Id,
                    new SaveAttendanceDraftRequest(0, teacher.Id, null, "Present", []), actor.Id, default);
                Assert.True(saved.IsSuccess);
                var read = await attendance.GetAsync(center.Id, lastBeforeLeaving.Id, default);
                Assert.Equal("Toán 9A", read.Value.ClassName);
                Assert.Equal("Nguyễn Văn A", read.Value.ActualTeacherName);
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
                DELETE FROM organization.centers WHERE id = @center;
                DELETE FROM iam.users WHERE id IN (@actor, @learner);
                """, connection);
            cleanup.Parameters.AddWithValue("center", center.Id);
            cleanup.Parameters.AddWithValue("actor", actor.Id);
            cleanup.Parameters.AddWithValue("learner", learner.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
