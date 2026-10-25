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

public sealed class RealPostgresAttendanceTests
{
    [LocalPostgresFact]
    public async Task Draft_submit_correction_and_historical_roster_are_consistent()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure the local E2E connection string.");
        var clock = new MutableClock();
        var actorId = Guid.NewGuid();
        var center = Center.Create("E2E Attendance Center", "E2E Attendance Center",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", "e2e_attendance@example.invalid", "0900000000", clock.UtcNow);
        center.Approve(actorId, clock.UtcNow);
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;
        try
        {
            Guid roomId;
            await using (var org = new OrganizationDbContext(orgOptions))
            {
                var room = Room.Create(center.Id, "AR1", "Attendance room", null, actorId, clock.UtcNow);
                roomId = room.Id;
                org.Centers.Add(center);
                org.Rooms.Add(room);
                await org.SaveChangesAsync();
            }

            Guid classId;
            Guid teacherId;
            Guid firstStudentId;
            Guid secondStudentId;
            Guid thirdStudentId;
            Guid sessionId;
            var start = clock.UtcNow.AddHours(1);
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var subject = Subject.Create(center.Id, "AS1", "Attendance subject", null, actorId, clock.UtcNow);
                var teachingClass = TeachingClass.Create(center.Id, subject.Id, "AC1", "Attendance class", 5,
                    actorId, clock.UtcNow);
                var teacher = Teacher.Create(center.Id, "AT1", "Attendance teacher", clock.UtcNow);
                var first = Student.Create(center.Id, "AST1", "First student", clock.UtcNow);
                var second = Student.Create(center.Id, "AST2", "Second student", clock.UtcNow);
                var third = Student.Create(center.Id, "AST3", "Late enrollment", clock.UtcNow);
                db.Subjects.Add(subject);
                db.Classes.Add(teachingClass);
                db.Teachers.Add(teacher);
                db.Students.AddRange(first, second, third);
                await db.SaveChangesAsync();
                classId = teachingClass.Id;
                teacherId = teacher.Id;
                firstStudentId = first.Id;
                secondStudentId = second.Id;
                thirdStudentId = third.Id;
                var store = new TeachingStore(db);
                var enrollment = new EnrollmentWorkflow(store, clock);
                Assert.True((await enrollment.EnrollAsync(center.Id, classId, firstStudentId, default)).IsSuccess);
                Assert.True((await enrollment.EnrollAsync(center.Id, classId, secondStudentId, default)).IsSuccess);
                var scheduled = await new SessionScheduler(store, clock).ScheduleAsync(center.Id,
                    classId, teacherId, roomId, start, start.AddHours(2), default);
                Assert.True(scheduled.IsSuccess);
                sessionId = scheduled.Value.Id;
            }

            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var store = new TeachingStore(db);
                var workflow = new AttendanceWorkflow(store, new AttendanceStore(db), clock);
                var preview = await workflow.GetAsync(center.Id, sessionId, default);
                Assert.True(preview.IsSuccess);
                Assert.Equal("NotStarted", preview.Value.Status);
                Assert.Equal(2, preview.Value.Students.Count);
                var draftRequest = new SaveAttendanceDraftRequest(0, teacherId, null, "Unknown", []);
                Assert.Equal(TeachingErrors.AttendanceNotStarted,
                    (await workflow.SaveDraftAsync(center.Id, sessionId, draftRequest, actorId, default)).Error);

                clock.UtcNow = start.AddMinutes(5);
                await using var firstWriter = new AcademicsDbContext(academicOptions);
                await using var secondWriter = new AcademicsDbContext(academicOptions);
                var racingRequest = draftRequest with { Overrides = [new(firstStudentId, "Present", null)] };
                var writes = await Task.WhenAll(
                    new AttendanceWorkflow(new TeachingStore(firstWriter), new AttendanceStore(firstWriter), clock)
                        .SaveDraftAsync(center.Id, sessionId, racingRequest, actorId, default),
                    new AttendanceWorkflow(new TeachingStore(secondWriter), new AttendanceStore(secondWriter), clock)
                        .SaveDraftAsync(center.Id, sessionId, racingRequest, actorId, default));
                var draft = Assert.Single(writes, x => x.IsSuccess);
                Assert.Equal(TeachingErrors.ConcurrentChange, Assert.Single(writes, x => x.IsFailure).Error);
                Assert.True(draft.IsSuccess);
                Assert.Equal(1, draft.Value.Version);
                Assert.Equal("Draft", draft.Value.Status);
                Assert.Equal(TeachingErrors.AttendanceIncomplete,
                    (await workflow.SubmitAsync(center.Id, sessionId, 1, actorId, default)).Error);
                var complete = await workflow.SaveDraftAsync(center.Id, sessionId,
                    new SaveAttendanceDraftRequest(1, teacherId, "Observed attendance", "Present",
                        [new(secondStudentId, "ExcusedAbsent", "Parent called")]), actorId, default);
                Assert.True(complete.IsSuccess);
                Assert.Equal(2, complete.Value.Version);
                var unchangedDraft = await workflow.SaveDraftAsync(center.Id, sessionId,
                    new SaveAttendanceDraftRequest(2, teacherId, "Observed attendance", "Present",
                        [new(secondStudentId, "ExcusedAbsent", "Parent called")]), actorId, default);
                Assert.True(unchangedDraft.IsSuccess);
                Assert.Equal(2, unchangedDraft.Value.Version);
                Assert.Equal(TeachingErrors.ConcurrentChange,
                    (await workflow.SaveDraftAsync(center.Id, sessionId, draftRequest, actorId, default)).Error);
                var submitted = await workflow.SubmitAsync(center.Id, sessionId, 2, actorId, default);
                Assert.True(submitted.IsSuccess);
                Assert.Equal("Submitted", submitted.Value.Status);
                Assert.Equal(3, submitted.Value.Version);
                Assert.Equal(TeachingErrors.AttendanceSubmitted,
                    (await workflow.SaveDraftAsync(center.Id, sessionId,
                        draftRequest with { ExpectedVersion = 3 }, actorId, default)).Error);
                var correction = await workflow.CorrectAsync(center.Id, sessionId,
                    new CorrectAttendanceRequest(3, teacherId, "Corrected after parent call",
                        "Parent provided proof of attendance", [new(secondStudentId, "Late", "Arrived late")]),
                    actorId, default);
                Assert.True(correction.IsSuccess);
                Assert.Equal(4, correction.Value.Version);
                Assert.Equal("Late", correction.Value.Students.Single(x => x.StudentId == secondStudentId).Mark);
                Assert.Equal(TeachingErrors.AttendanceUnchanged,
                    (await workflow.CorrectAsync(center.Id, sessionId,
                        new CorrectAttendanceRequest(4, teacherId, "Corrected after parent call",
                            "Checking that unchanged correction is rejected", []), actorId, default)).Error);
                var noteOnly = await workflow.CorrectAsync(center.Id, sessionId,
                    new CorrectAttendanceRequest(4, teacherId, "Administrative note corrected",
                        "Corrected the session note without changing marks", []), actorId, default);
                Assert.True(noteOnly.IsSuccess);
                Assert.Equal(5, noteOnly.Value.Version);
                Assert.Equal(TeachingErrors.ConcurrentChange,
                    (await workflow.CorrectAsync(center.Id, sessionId,
                        new CorrectAttendanceRequest(3, teacherId, null, "Stale correction attempt",
                            [new(secondStudentId, "Present", null)]), actorId, default)).Error);
                var history = await workflow.HistoryAsync(center.Id, sessionId, default);
                Assert.True(history.IsSuccess);
                Assert.Equal(["Submitted", "Corrected", "Corrected"],
                    history.Value.Select(x => x.Action).ToArray());
                Assert.Equal("Parent provided proof of attendance", history.Value[^2].Reason);
                Assert.Equal(TeachingErrors.NotFound,
                    (await workflow.GetAsync(Guid.NewGuid(), sessionId, default)).Error);

                var enrollment = new EnrollmentWorkflow(store, clock);
                Assert.True((await enrollment.WithdrawAsync(center.Id, classId, firstStudentId, "Changed to another class", actorId, default)).IsSuccess);
                Assert.True((await enrollment.EnrollAsync(center.Id, classId, thirdStudentId, default)).IsSuccess);
                var historical = await workflow.GetAsync(center.Id, sessionId, default);
                Assert.Equal(2, historical.Value.Students.Count);
                Assert.Contains(historical.Value.Students, x => x.StudentId == firstStudentId);
                Assert.DoesNotContain(historical.Value.Students, x => x.StudentId == thirdStudentId);
                Assert.Equal(TeachingErrors.StartedSession,
                    (await new SessionScheduler(store, clock).RescheduleAsync(center.Id, sessionId,
                        teacherId, roomId, start.AddDays(1), start.AddDays(1).AddHours(2), 0, default)).Error);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                DELETE FROM academics.attendance_revisions WHERE center_id = @center;
                DELETE FROM academics.attendance_entries WHERE center_id = @center;
                DELETE FROM academics.attendance_sheets WHERE center_id = @center;
                DELETE FROM academics.teaching_sessions WHERE center_id = @center;
                DELETE FROM academics.enrollments WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.students WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id = @center AND contact_email = 'e2e_attendance@example.invalid';
                """, connection);
            command.Parameters.AddWithValue("center", center.Id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }
}
