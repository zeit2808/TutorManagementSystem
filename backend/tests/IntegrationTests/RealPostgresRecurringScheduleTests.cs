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
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

public sealed class RealPostgresRecurringScheduleTests
{
    [LocalPostgresFact]
    public async Task Recurring_schedule_is_atomic_retryable_and_editable_on_real_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure the local E2E connection string.");
        var clock = new TestClock();
        var center = Center.Create("E2E Schedule Center", "E2E Schedule Center",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture), null,
            "Test only", "e2e_schedule@example.invalid", "0900000000", clock.UtcNow);
        center.Approve(Guid.NewGuid(), clock.UtcNow);
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicsOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            Guid firstRoomId;
            Guid secondRoomId;
            await using (var org = new OrganizationDbContext(orgOptions))
            {
                var firstRoom = Room.Create(center.Id, "R1", "Room one", null, Guid.NewGuid(), clock.UtcNow);
                var secondRoom = Room.Create(center.Id, "R2", "Room two", null, Guid.NewGuid(), clock.UtcNow);
                firstRoomId = firstRoom.Id;
                secondRoomId = secondRoom.Id;
                org.Centers.Add(center);
                org.Rooms.AddRange(firstRoom, secondRoom);
                await org.SaveChangesAsync();
            }

            await using var db = new AcademicsDbContext(academicsOptions);
            var subject = Subject.Create(center.Id, "S1", "Subject", null, Guid.NewGuid(), clock.UtcNow);
            var firstClass = TeachingClass.Create(center.Id, subject.Id, "C1", "Class one", 20,
                Guid.NewGuid(), clock.UtcNow);
            var secondClass = TeachingClass.Create(center.Id, subject.Id, "C2", "Class two", 20,
                Guid.NewGuid(), clock.UtcNow);
            var firstTeacher = Teacher.Create(center.Id, "T1", "First teacher", clock.UtcNow);
            var secondTeacher = Teacher.Create(center.Id, "T2", "Second teacher", clock.UtcNow);
            db.Subjects.Add(subject);
            db.Classes.AddRange(firstClass, secondClass);
            db.Teachers.AddRange(firstTeacher, secondTeacher);
            await db.SaveChangesAsync();

            var store = new TeachingStore(db);
            var schedules = new RecurringScheduleWorkflow(store, clock);
            var single = new SessionScheduler(store, clock);
            var startDate = NextMonday(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(14));
            var excluded = startDate.AddDays(7);
            var request = new CreateScheduleSeriesRequest(Guid.NewGuid(), firstClass.Id,
                firstTeacher.Id, firstRoomId, "Asia/Ho_Chi_Minh", startDate,
                startDate.AddDays(21), new TimeOnly(17, 0), 120, ["Monday"], [excluded]);

            var preview = await schedules.PreviewAsync(center.Id, request, default);
            Assert.True(preview.IsSuccess);
            Assert.Equal(3, preview.Value.SessionCount);
            Assert.All(preview.Value.Occurrences, x => Assert.Empty(x.Conflicts));
            Assert.DoesNotContain(preview.Value.Occurrences, x => x.OccurrenceDate == excluded);
            Assert.Equal(new TimeOnly(10, 0), TimeOnly.FromDateTime(preview.Value.Occurrences[0].StartUtc.UtcDateTime));

            var created = await schedules.CreateAsync(center.Id, request, default);
            Assert.True(created.IsSuccess);
            Assert.False(created.Value.Replayed);
            Assert.Equal(3, created.Value.SessionsCreated);
            var seriesId = created.Value.Series.Id;
            var replay = await schedules.CreateAsync(center.Id, request, default);
            Assert.True(replay.IsSuccess);
            Assert.True(replay.Value.Replayed);
            Assert.Equal(seriesId, replay.Value.Series.Id);
            var changedRequest = request with { RoomId = secondRoomId };
            Assert.Equal(TeachingErrors.RequestIdReused,
                (await schedules.CreateAsync(center.Id, changedRequest, default)).Error);
            Assert.Equal(3, await db.Sessions.CountAsync(x => x.SeriesId == seriesId));
            Assert.Equal(TeachingErrors.NotFound, (await schedules.GetAsync(Guid.NewGuid(), seriesId, default)).Error);

            var firstStart = preview.Value.Occurrences[0].StartUtc;
            var classConflictRequest = request with
            {
                RequestId = Guid.NewGuid(), TeacherId = secondTeacher.Id,
                RoomId = secondRoomId, LocalStartTime = new TimeOnly(18, 0)
            };
            var classConflictPreview = await schedules.PreviewAsync(center.Id, classConflictRequest, default);
            Assert.Contains(classConflictPreview.Value.Occurrences[0].Conflicts,
                x => x.ResourceType == "Class");
            Assert.Equal(TeachingErrors.ScheduleConflict,
                (await schedules.CreateAsync(center.Id, classConflictRequest, default)).Error);
            Assert.Null(await db.ScheduleSeries.SingleOrDefaultAsync(x => x.RequestId == classConflictRequest.RequestId));

            var roomConflictRequest = request with
            {
                RequestId = Guid.NewGuid(), ClassId = secondClass.Id,
                TeacherId = secondTeacher.Id, LocalStartTime = new TimeOnly(18, 0)
            };
            var roomConflictPreview = await schedules.PreviewAsync(center.Id, roomConflictRequest, default);
            Assert.Contains(roomConflictPreview.Value.Occurrences[0].Conflicts,
                x => x.ResourceType == "Room");

            var overnightRequest = request with
            {
                RequestId = Guid.NewGuid(), ClassId = secondClass.Id,
                TeacherId = secondTeacher.Id, RoomId = secondRoomId,
                StartDate = startDate, EndDate = startDate,
                LocalStartTime = new TimeOnly(23, 30), ExcludedDates = []
            };
            var overnight = await schedules.PreviewAsync(center.Id, overnightRequest, default);
            Assert.True(overnight.IsSuccess);
            Assert.Equal(startDate.AddDays(1).ToDateTime(new TimeOnly(1, 30)),
                TimeZoneInfo.ConvertTime(overnight.Value.Occurrences[0].EndUtc,
                    TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh")).DateTime);

            var conflict = await single.ScheduleAsync(center.Id, secondClass.Id, firstTeacher.Id,
                secondRoomId, firstStart.AddMinutes(30), firstStart.AddHours(1), default);
            Assert.Equal(TeachingErrors.ScheduleConflict, conflict.Error);

            await using (var direct = new AcademicsDbContext(academicsOptions))
            {
                direct.Sessions.Add(TeachingSession.Create(center.Id, secondClass.Id, firstTeacher.Id,
                    secondRoomId, firstStart.AddMinutes(30), firstStart.AddHours(1), clock.UtcNow));
                var databaseConflict = await Assert.ThrowsAsync<DbUpdateException>(() => direct.SaveChangesAsync());
                Assert.Equal(PostgresErrorCodes.ExclusionViolation,
                    Assert.IsType<PostgresException>(databaseConflict.InnerException).SqlState);
            }

            var seriesSessions = await db.Sessions.Where(x => x.SeriesId == seriesId)
                .OrderBy(x => x.OccurrenceDate).ToArrayAsync();
            var firstSession = seriesSessions[0];
            var firstDayBlocker = await single.ScheduleAsync(center.Id, secondClass.Id,
                secondTeacher.Id, secondRoomId, firstStart.AddMinutes(30),
                firstStart.AddHours(2).AddMinutes(30), default);
            Assert.True(firstDayBlocker.IsSuccess);
            var cannotMove = await single.RescheduleAsync(center.Id, firstSession.Id,
                firstTeacher.Id, secondRoomId, firstStart.AddMinutes(30),
                firstStart.AddHours(2).AddMinutes(30), 0, default);
            Assert.Equal(TeachingErrors.ScheduleConflict, cannotMove.Error);
            var moved = await single.RescheduleAsync(center.Id, firstSession.Id,
                firstTeacher.Id, firstRoomId, firstStart.AddHours(3),
                firstStart.AddHours(5), 0, default);
            Assert.True(moved.IsSuccess);
            Assert.True(moved.Value.IsRescheduled);
            Assert.Equal(startDate, moved.Value.OccurrenceDate);
            Assert.Equal(1, moved.Value.Version);
            Assert.Equal(TeachingErrors.ConcurrentChange,
                (await single.RescheduleAsync(center.Id, firstSession.Id,
                    firstTeacher.Id, firstRoomId, firstStart.AddHours(6),
                    firstStart.AddHours(8), 0, default)).Error);

            var cutoff = startDate.AddDays(14);
            var blockerStart = preview.Value.Occurrences[1].StartUtc.AddHours(1);
            var blocker = await single.ScheduleAsync(center.Id, secondClass.Id, secondTeacher.Id,
                secondRoomId, blockerStart, blockerStart.AddHours(1), default);
            Assert.True(blocker.IsSuccess);
            var replacement = new CreateScheduleSeriesRequest(Guid.NewGuid(), firstClass.Id,
                secondTeacher.Id, secondRoomId, "Asia/Ho_Chi_Minh", cutoff,
                startDate.AddDays(21), new TimeOnly(18, 0), 120, ["Monday"], []);
            var replaceRequest = new ReplaceScheduleSeriesRequest(cutoff, 0, replacement);
            var failedReplace = await schedules.ReplaceFutureAsync(center.Id, seriesId, replaceRequest, default);
            Assert.Equal(TeachingErrors.ScheduleConflict, failedReplace.Error);
            await using (var verifyRollback = new AcademicsDbContext(academicsOptions))
            {
                Assert.Null((await verifyRollback.ScheduleSeries.SingleAsync(x => x.Id == seriesId)).StoppedFromDate);
                Assert.Equal(3, await verifyRollback.Sessions.CountAsync(x => x.SeriesId == seriesId &&
                    x.CancelledAtUtc == null));
            }

            Assert.True((await single.CancelAsync(center.Id, blocker.Value.Id, 0, default)).IsSuccess);
            // A failed transaction leaves tracked entities dirty; a fresh context models the next HTTP request.
            await using var replaceDb = new AcademicsDbContext(academicsOptions);
            var replaceFlow = new RecurringScheduleWorkflow(new TeachingStore(replaceDb), clock);
            var replaced = await replaceFlow.ReplaceFutureAsync(center.Id, seriesId, replaceRequest, default);
            Assert.True(replaced.IsSuccess);
            Assert.Equal(2, replaced.Value.SessionsCancelled);
            Assert.Equal(2, replaced.Value.Replacement.SessionsCreated);
            Assert.Equal(cutoff, replaced.Value.PreviousSeries.StoppedFromDate);
            var replacementId = replaced.Value.Replacement.Series.Id;
            var replaceReplay = await replaceFlow.ReplaceFutureAsync(center.Id, seriesId, replaceRequest, default);
            Assert.True(replaceReplay.IsSuccess);
            Assert.True(replaceReplay.Value.Replacement.Replayed);

            await using var stopDb = new AcademicsDbContext(academicsOptions);
            var stopFlow = new RecurringScheduleWorkflow(new TeachingStore(stopDb), clock);
            var stopped = await stopFlow.StopAsync(center.Id, replacementId,
                startDate.AddDays(21), 0, default);
            Assert.True(stopped.IsSuccess);
            Assert.Equal(1, stopped.Value.SessionsCancelled);
            Assert.Equal(TeachingErrors.ConcurrentChange,
                (await stopFlow.StopAsync(center.Id, replacementId,
                    startDate.AddDays(21), 0, default)).Error);

            await using var final = new AcademicsDbContext(academicsOptions);
            Assert.Equal(1, await final.Sessions.CountAsync(x => x.SeriesId == seriesId && x.CancelledAtUtc == null));
            Assert.Equal(1, await final.Sessions.CountAsync(x => x.SeriesId == replacementId && x.CancelledAtUtc == null));
            Assert.Equal(5, await final.Sessions.CountAsync(x => x.CenterId == center.Id && x.SeriesId != null));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                DELETE FROM academics.teaching_sessions WHERE center_id = @center;
                DELETE FROM academics.schedule_series WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id = @center AND contact_email = 'e2e_schedule@example.invalid';
                """, connection);
            command.Parameters.AddWithValue("center", center.Id);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("America/New_York")]
    [InlineData("UTC")]
    public async Task Schedules_outside_vietnam_time_are_rejected(string zone)
    {
        // D12: every center runs on Vietnam time (no daylight saving), so other zones are refused.
        var request = new CreateScheduleSeriesRequest(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), zone,
            new DateOnly(2027, 3, 14), new DateOnly(2027, 3, 14),
            new TimeOnly(1, 30), 120, ["Sunday"], []);
        var workflow = new RecurringScheduleWorkflow(null!, new TestClock());
        var result = await workflow.PreviewAsync(Guid.NewGuid(), request, default);
        Assert.Equal(TeachingErrors.InvalidSeries, result.Error);
    }

    [Fact]
    public async Task Recurrence_bounds_and_all_excluded_days_are_rejected_before_database_access()
    {
        var clock = new TestClock();
        var monday = NextMonday(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(1));
        var template = new CreateScheduleSeriesRequest(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), "Asia/Ho_Chi_Minh", monday, monday,
            new TimeOnly(17, 0), 120, ["Monday"], []);
        var workflow = new RecurringScheduleWorkflow(null!, clock);
        Assert.Equal(TeachingErrors.NoOccurrences,
            (await workflow.PreviewAsync(Guid.NewGuid(),
                template with { ExcludedDates = [monday] }, default)).Error);
        Assert.Equal(TeachingErrors.InvalidSeries,
            (await workflow.PreviewAsync(Guid.NewGuid(),
                template with { Weekdays = ["Monday", "monday"] }, default)).Error);
        var farDate = NextMonday(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(380));
        Assert.Equal(TeachingErrors.ScheduleTooFar,
            (await workflow.PreviewAsync(Guid.NewGuid(),
                template with { StartDate = farDate, EndDate = farDate }, default)).Error);
        var oldMonday = NextMonday(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(-14));
        Assert.Equal(TeachingErrors.ScheduleNotFuture,
            (await workflow.PreviewAsync(Guid.NewGuid(),
                template with { StartDate = oldMonday, EndDate = oldMonday }, default)).Error);
    }

    private static DateOnly NextMonday(DateOnly date) =>
        date.AddDays(((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7);

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }
}
