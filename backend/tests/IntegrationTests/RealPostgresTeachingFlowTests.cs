using System.Security.Cryptography;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Academics.Application.Subjects.ArchiveSubject;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

namespace TutorManagement.IntegrationTests;

public sealed class RealPostgresTeachingFlowTests
{
    [LocalPostgresFact]
    public async Task Class_capacity_enrollment_and_session_conflicts_work_on_real_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure the local E2E connection string.");
        var center = Center.Create("E2E Teaching Center", "E2E Teaching Center",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture), null,
            "Test only", "e2e_teaching@example.invalid", "0900000000", DateTimeOffset.UtcNow);
        var actorId = Guid.NewGuid();
        var clock = new TestClock();
        center.Approve(actorId, clock.UtcNow);
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                await organization.Centers.AddAsync(center);
                await organization.Rooms.AddAsync(Room.Create(center.Id, "R1", "Test room", null, actorId, clock.UtcNow));
                await organization.SaveChangesAsync();
            }

            await using var db = new AcademicsDbContext(academicOptions);
            var subject = Subject.Create(center.Id, "S1", "Test subject", null, actorId, clock.UtcNow);
            await db.Subjects.AddAsync(subject);
            await db.SaveChangesAsync();
            var store = new TeachingStore(db);
            var subjectGuard = new SubjectClassGuard(db);
            var classes = new ClassCatalog(store, subjectGuard, clock);
            var people = new PeopleCatalog(store, clock);
            var enrollments = new EnrollmentWorkflow(store, clock);
            var scheduler = new SessionScheduler(store, clock);

            var group = await classes.CreateAsync(center.Id, subject.Id, "C1", "Test class", 1, actorId, default);
            Assert.True(group.IsSuccess);
            var archiveSubject = new ArchiveSubjectCommandHandler(new SubjectRepository(db),
                (IAcademicsUnitOfWork)db, subjectGuard, clock);
            var cannotArchiveSubject = await archiveSubject.HandleAsync(
                new(center.Id, subject.Id, actorId, subject.Version), default);
            Assert.Equal(SubjectErrors.HasActiveClasses, cannotArchiveSubject.Error);
            var firstStudent = await people.CreateStudentAsync(center.Id, "ST1", "First student", "Parent One", "0912345678", default);
            var secondStudent = await people.CreateStudentAsync(center.Id, "ST2", "Second student", "Parent Two", "0987654321", default);
            var teacher = await people.CreateTeacherAsync(center.Id, "TC1", "Test teacher", default);
            Assert.True(firstStudent.IsSuccess && secondStudent.IsSuccess && teacher.IsSuccess);

            var firstClassPage = await classes.ListAsync(center.Id, false, 1, 1, default);
            Assert.Equal(1, firstClassPage.TotalCount);
            Assert.Single(firstClassPage.Items);
            var studentPage = await people.ListStudentsAsync(center.Id, false, 2, 1, default);
            Assert.Equal(2, studentPage.TotalCount);
            Assert.Single(studentPage.Items);
            var teacherPage = await people.ListTeachersAsync(center.Id, false, 1, 25, default);
            Assert.Single(teacherPage.Items);

            var first = await enrollments.EnrollAsync(center.Id, group.Value.Id, firstStudent.Value.Id, default);
            Assert.True(first.IsSuccess);
            var roster = await enrollments.ListAsync(center.Id, group.Value.Id, EnrollmentFilter.Active, 1, 25, default);
            Assert.True(roster.IsSuccess);
            var firstEntry = Assert.Single(roster.Value.Items);
            Assert.Equal(first.Value.Id, firstEntry.EnrollmentId);
            Assert.Equal("ST1", firstEntry.StudentCode);
            Assert.Equal("First student", firstEntry.FullName);
            Assert.Equal("Active", firstEntry.Status);
            Assert.Null(firstEntry.WithdrawnAtUtc);
            var cannotArchiveEnrolled = await classes.ArchiveAsync(center.Id, group.Value.Id, 1, default);
            Assert.Equal(TeachingErrors.HasEnrollments, cannotArchiveEnrolled.Error);
            var full = await enrollments.EnrollAsync(center.Id, group.Value.Id, secondStudent.Value.Id, default);
            Assert.Equal(TeachingErrors.Full, full.Error);
            var duplicate = await enrollments.EnrollAsync(center.Id, group.Value.Id, firstStudent.Value.Id, default);
            Assert.Equal(TeachingErrors.AlreadyEnrolled, duplicate.Error);

            // Both requests read a free seat. Only the first UPDATE may advance the
            // class version; the second transaction must roll back its enrollment.
            var raceClass = await classes.CreateAsync(center.Id, subject.Id, "C2", "Race class", 1, actorId, default);
            Assert.True(raceClass.IsSuccess);
            await using (var firstWriter = new AcademicsDbContext(academicOptions))
            await using (var secondWriter = new AcademicsDbContext(academicOptions))
            {
                var firstView = await firstWriter.Classes.SingleAsync(x => x.Id == raceClass.Value.Id);
                var secondView = await secondWriter.Classes.SingleAsync(x => x.Id == raceClass.Value.Id);
                firstView.Enroll(clock.UtcNow);
                secondView.Enroll(clock.UtcNow);
                firstWriter.Enrollments.Add(Enrollment.Create(center.Id, raceClass.Value.Id, firstStudent.Value.Id, clock.UtcNow));
                secondWriter.Enrollments.Add(Enrollment.Create(center.Id, raceClass.Value.Id, secondStudent.Value.Id, clock.UtcNow));
                await firstWriter.SaveChangesAsync();
                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondWriter.SaveChangesAsync());
            }
            await using (var verify = new AcademicsDbContext(academicOptions))
            {
                Assert.Equal(1, await verify.Classes.Where(x => x.Id == raceClass.Value.Id)
                    .Select(x => x.EnrolledCount).SingleAsync());
                Assert.Equal(1, await verify.Enrollments.CountAsync(x => x.ClassId == raceClass.Value.Id));
            }

            await using var organizationRead = new OrganizationDbContext(orgOptions);
            var roomId = await organizationRead.Rooms.Where(x => x.CenterId == center.Id).Select(x => x.Id).SingleAsync();
            var start = clock.UtcNow.AddDays(1);
            var scheduled = await scheduler.ScheduleAsync(center.Id, group.Value.Id, teacher.Value.Id,
                roomId, start, start.AddHours(2), default);
            Assert.True(scheduled.IsSuccess);
            var busyTeacher = await people.ArchiveTeacherAsync(center.Id, teacher.Value.Id, 0, default);
            Assert.Equal(TeachingErrors.HasFutureSessions, busyTeacher.Error);
            await using var organizationForArchive = new OrganizationDbContext(orgOptions);
            var roomArchiver = new ArchiveRoomCommandHandler(new OrganizationCenterReader(organizationForArchive),
                new RoomRepository(organizationForArchive), (IOrganizationUnitOfWork)organizationForArchive,
                new RoomArchiveGuard(organizationForArchive), clock);
            var busyRoom = await roomArchiver.HandleAsync(new(center.Id, roomId, actorId, 0), default);
            Assert.Equal(RoomErrors.HasFutureSessions, busyRoom.Error);
            var wrongRoom = await scheduler.ScheduleAsync(center.Id, group.Value.Id, teacher.Value.Id,
                Guid.NewGuid(), start.AddHours(4), start.AddHours(5), default);
            Assert.Equal(TeachingErrors.RoomUnavailable, wrongRoom.Error);
            var overlapping = await scheduler.ScheduleAsync(center.Id, group.Value.Id, teacher.Value.Id,
                roomId, start.AddHours(1), start.AddHours(3), default);
            Assert.Equal(TeachingErrors.ScheduleConflict, overlapping.Error);
            var adjacent = await scheduler.ScheduleAsync(center.Id, group.Value.Id, teacher.Value.Id,
                roomId, start.AddHours(2), start.AddHours(3), default);
            Assert.True(adjacent.IsSuccess);

            var withdrawn = await enrollments.WithdrawAsync(center.Id, group.Value.Id, firstStudent.Value.Id, "Family moved away", actorId, default);
            Assert.True(withdrawn.IsSuccess);
            var freePlace = await enrollments.EnrollAsync(center.Id, group.Value.Id, secondStudent.Value.Id, default);
            Assert.True(freePlace.IsSuccess);

            var active = await enrollments.ListAsync(center.Id, group.Value.Id, EnrollmentFilter.Active, 1, 25, default);
            Assert.Equal(secondStudent.Value.Id, Assert.Single(active.Value.Items).StudentId);
            var history = await enrollments.ListAsync(center.Id, group.Value.Id, EnrollmentFilter.Withdrawn, 1, 25, default);
            var formerStudent = Assert.Single(history.Value.Items);
            Assert.Equal(firstStudent.Value.Id, formerStudent.StudentId);
            Assert.Equal("Withdrawn", formerStudent.Status);
            Assert.NotNull(formerStudent.WithdrawnAtUtc);
            var all = await enrollments.ListAsync(center.Id, group.Value.Id, EnrollmentFilter.All, 2, 1, default);
            Assert.Equal(2, all.Value.TotalCount);
            Assert.Single(all.Value.Items);
            var wrongCenter = await enrollments.ListAsync(Guid.NewGuid(), group.Value.Id,
                EnrollmentFilter.All, 1, 25, default);
            Assert.Equal(TeachingErrors.NotFound, wrongCenter.Error);

            var cancelledFirst = await scheduler.CancelAsync(center.Id, scheduled.Value.Id, 0, default);
            var cancelledSecond = await scheduler.CancelAsync(center.Id, adjacent.Value.Id, 0, default);
            Assert.True(cancelledFirst.IsSuccess && cancelledSecond.IsSuccess);
            var archivedRoom = await roomArchiver.HandleAsync(new(center.Id, roomId, actorId, 0), default);
            Assert.True(archivedRoom.IsSuccess);
            var withdrawnSecond = await enrollments.WithdrawAsync(center.Id, group.Value.Id, secondStudent.Value.Id, "Family moved away", actorId, default);
            Assert.True(withdrawnSecond.IsSuccess);
            var archivedClass = await classes.ArchiveAsync(center.Id, group.Value.Id, 4, default);
            Assert.True(archivedClass.IsSuccess);
            var archivedTeacher = await people.ArchiveTeacherAsync(center.Id, teacher.Value.Id, 0, default);
            Assert.True(archivedTeacher.IsSuccess);
            Assert.DoesNotContain((await classes.ListAsync(center.Id, false, 1, 25, default)).Items,
                x => x.Id == group.Value.Id);
            Assert.Contains((await classes.ListAsync(center.Id, true, 1, 25, default)).Items,
                x => x.Id == group.Value.Id);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                DELETE FROM academics.teaching_sessions WHERE center_id = @center;
                DELETE FROM academics.enrollments WHERE center_id = @center;
                DELETE FROM academics.classes WHERE center_id = @center;
                DELETE FROM academics.students WHERE center_id = @center;
                DELETE FROM academics.teachers WHERE center_id = @center;
                DELETE FROM academics.subjects WHERE center_id = @center;
                DELETE FROM organization.rooms WHERE center_id = @center;
                DELETE FROM organization.centers WHERE id = @center AND contact_email = 'e2e_teaching@example.invalid';
                """, connection);
            command.Parameters.AddWithValue("center", center.Id);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
    }
}
