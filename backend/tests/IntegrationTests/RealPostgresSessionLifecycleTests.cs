using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
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
/// Wave 4: cancelling and rescheduling need a reason, a started session can be recorded as not held,
/// a missed session gets at most one makeup, and the public directory shows only opted-in centers.
/// </summary>
public sealed class RealPostgresSessionLifecycleTests
{
    [LocalPostgresFact]
    public async Task Sessions_record_reasons_not_held_and_makeups_and_directory_lists_open_classes()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = DateTimeOffset.UtcNow;
        var t0 = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var past = t0.AddDays(-30);
        var clock = new FixedClock(t0);
        var actor = User.CreateExternal($"owner-{Guid.NewGuid():N}@example.invalid", past);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var center = Center.Create("Lifecycle LLC", $"Trung tâm {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"lifecycle-{Guid.NewGuid():N}@example.invalid", "0900000000", past);
        center.Approve(actor.Id, past);
        var hidden = Center.Create("Hidden LLC", $"Ẩn {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"hidden-{Guid.NewGuid():N}@example.invalid", "0900000001", past);
        hidden.Approve(actor.Id, past);
        center.SetDirectoryListing(true, past);
        var room = Room.Create(center.Id, "R1", "Phòng 101", null, actor.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.Add(actor);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.AddRange(center, hidden);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
            }

            var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, actor.Id, past);
            var group = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 12, actor.Id, past);
            group.SetRegistrationOpen(true, past);
            var closed = TeachingClass.Create(center.Id, subject.Id, "T9B", "Toán 9B", 12, actor.Id, past);
            var teacher = Teacher.Create(center.Id, "GV1", "Nguyễn Văn A", past);
            var started = TeachingSession.Create(center.Id, group.Id, teacher.Id, room.Id,
                t0.AddHours(-1), t0.AddMinutes(30), past);
            var future = TeachingSession.Create(center.Id, group.Id, teacher.Id, room.Id,
                t0.AddDays(2), t0.AddDays(2).AddHours(1), past);
            var later = TeachingSession.Create(center.Id, group.Id, teacher.Id, room.Id,
                t0.AddDays(9), t0.AddDays(9).AddHours(1), past);
            await using (var academic = new AcademicsDbContext(academicOptions))
            {
                academic.Subjects.Add(subject);
                academic.Classes.AddRange(group, closed);
                academic.Teachers.Add(teacher);
                academic.Sessions.AddRange(started, future, later);
                await academic.SaveChangesAsync();
            }

            SessionScheduler Scheduler(AcademicsDbContext db) => new(new TeachingStore(db), clock);

            // D7: a reason is required, and a started session cannot be cancelled.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var scheduler = Scheduler(db);
                Assert.Equal(TeachingErrors.InvalidSessionReason,
                    (await scheduler.CancelAsync(center.Id, future.Id, 0, "  ", actor.Id, default)).Error);
                Assert.Equal(TeachingErrors.Invalid,
                    (await scheduler.CancelAsync(center.Id, started.Id, 0, "Giáo viên ốm", actor.Id, default)).Error);
                Assert.Equal(TeachingErrors.SessionNotStarted,
                    (await scheduler.MarkNotHeldAsync(center.Id, future.Id, 0, "Mất điện", actor.Id, default)).Error);
            }

            // D6: a started session is recorded as not held, with its reason.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var notHeld = await Scheduler(db).MarkNotHeldAsync(center.Id, started.Id, 0, "  Mất điện  ", actor.Id, default);
                Assert.True(notHeld.IsSuccess);
                Assert.Equal("NotHeld", notHeld.Value.Status);
                Assert.Equal("Mất điện", notHeld.Value.NotHeldReason);
                Assert.False(notHeld.Value.IsActive);
            }

            // A makeup must replace a missed session of the same class, and only once.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var invalid = await Scheduler(db).ScheduleAsync(center.Id, group.Id, teacher.Id, room.Id,
                    t0.AddDays(3), t0.AddDays(3).AddHours(1), later.Id, default);
                Assert.Equal(TeachingErrors.InvalidMakeup, invalid.Error);
            }
            Guid makeupId;
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var makeup = await Scheduler(db).ScheduleAsync(center.Id, group.Id, teacher.Id, room.Id,
                    t0.AddDays(3), t0.AddDays(3).AddHours(1), started.Id, default);
                Assert.True(makeup.IsSuccess);
                Assert.Equal(started.Id, makeup.Value.MakeupForSessionId);
                makeupId = makeup.Value.Id;
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var second = await Scheduler(db).ScheduleAsync(center.Id, group.Id, teacher.Id, room.Id,
                    t0.AddDays(4), t0.AddDays(4).AddHours(1), started.Id, default);
                Assert.Equal(TeachingErrors.MakeupExists, second.Error);
            }

            // Cancelling and rescheduling keep the reason given.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var cancelled = await Scheduler(db).CancelAsync(center.Id, future.Id, 0, "Nghỉ lễ", actor.Id, default);
                Assert.True(cancelled.IsSuccess);
                Assert.Equal("Cancelled", cancelled.Value.Status);
                Assert.Equal("Nghỉ lễ", cancelled.Value.CancellationReason);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var moved = await Scheduler(db).RescheduleAsync(center.Id, later.Id, teacher.Id, room.Id,
                    later.StartUtc.AddHours(2), later.EndUtc.AddHours(2), 0, "Đổi giờ theo phụ huynh", actor.Id, default);
                Assert.True(moved.IsSuccess);
                Assert.Equal("Đổi giờ theo phụ huynh", moved.Value.RescheduleReason);
                Assert.True(moved.Value.IsRescheduled);
            }

            // D4: only the opted-in center appears, with its open (not closed) class and its dates.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var directory = new CourseDirectory(new CourseDirectoryStore(db), clock);
                var page = await directory.ListCentersAsync(marker, null, 1, 20, default);
                var listed = Assert.Single(page.Items);
                Assert.Equal(center.Id, listed.Id);
                Assert.Equal(1, listed.OpenClassCount);
                Assert.Contains($"Toán {marker}", listed.Subjects);

                Assert.Empty((await directory.ListCentersAsync(marker, "Hoá học", 1, 20, default)).Items);
                Assert.Contains($"Toán {marker}", await directory.ListSubjectsAsync(default));

                var detail = await directory.GetCenterAsync(center.Id, null, default);
                Assert.True(detail.IsSuccess);
                var item = Assert.Single(detail.Value.Classes);
                Assert.Equal(group.Id, item.Id);
                Assert.Equal(12, item.SeatsLeft);
                Assert.Equal("Nguyễn Văn A", Assert.Single(item.TeacherNames));
                Assert.NotNull(item.StartDate);
                Assert.True(item.EndDate >= item.StartDate);

                Assert.Equal(CourseDirectory.CenterNotListed, (await directory.GetCenterAsync(hidden.Id, null, default)).Error);
            }
            Assert.NotEqual(Guid.Empty, makeupId);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM academics.teaching_sessions WHERE center_id = @center AND makeup_for_session_id IS NOT NULL;
                DELETE FROM academics.teaching_sessions WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id IN (@center, @hidden);
                DELETE FROM iam.users WHERE id = @actor;
                """, connection);
            cleanup.Parameters.AddWithValue("center", center.Id);
            cleanup.Parameters.AddWithValue("hidden", hidden.Id);
            cleanup.Parameters.AddWithValue("actor", actor.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
