using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
/// QUY-TAC waves 1–3 against real PostgreSQL: the personal schedule across centers, schedule conflicts when a
/// student joins a class, the change log with notices and emails, and student requests with their effects.
/// </summary>
public sealed class RealPostgresPersonalScheduleTests
{
    [LocalPostgresFact]
    public async Task Personal_schedule_conflicts_notices_and_requests_work_across_centers()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = DateTimeOffset.UtcNow;
        var t0 = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var past = t0.AddDays(-30);
        var clock = new FixedClock(t0);
        var owner = User.CreateExternal($"owner-{Guid.NewGuid():N}@example.invalid", past);
        var studentEmail = $"student-{Guid.NewGuid():N}@example.invalid";
        var studentUser = User.CreateExternal(studentEmail, past);
        var teacherUser = User.CreateExternal($"teacher-{Guid.NewGuid():N}@example.invalid", past);
        var marker = Guid.NewGuid().ToString("N")[..8];
        Center NewCenter(string name) => Center.Create($"{name} LLC", $"{name} {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@example.invalid", "0900000000", past);
        var centerA = NewCenter("Alpha");
        var centerB = NewCenter("Beta");
        centerA.Approve(owner.Id, past);
        centerB.Approve(owner.Id, past);
        var roomA1 = Room.Create(centerA.Id, "A101", "Phòng A101", null, owner.Id, past);
        var roomA2 = Room.Create(centerA.Id, "A102", "Phòng A102", null, owner.Id, past);
        var roomA3 = Room.Create(centerA.Id, "A103", "Phòng A103", null, owner.Id, past);
        var roomB1 = Room.Create(centerB.Id, "B101", "Phòng B101", null, owner.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var academicOptions = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(owner, studentUser, teacherUser);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.AddRange(centerA, centerB);
                organization.Rooms.AddRange(roomA1, roomA2, roomA3, roomB1);
                await organization.SaveChangesAsync();
            }

            var subjectA = Subject.Create(centerA.Id, "MATH", $"Toán {marker}", null, owner.Id, past);
            var subjectB = Subject.Create(centerB.Id, "ENG", $"Anh {marker}", null, owner.Id, past);
            var a1 = TeachingClass.Create(centerA.Id, subjectA.Id, "A1", "Toán 9A", 20, owner.Id, past);
            var a2 = TeachingClass.Create(centerA.Id, subjectA.Id, "A2", "Toán 9B", 20, owner.Id, past);
            var a3 = TeachingClass.Create(centerA.Id, subjectA.Id, "A3", "Toán 9C", 20, owner.Id, past);
            var b1 = TeachingClass.Create(centerB.Id, subjectB.Id, "B1", "Anh 9", 20, owner.Id, past);
            a2.SetRegistrationOpen(true, past);
            var teacherA = Teacher.Create(centerA.Id, "GVA", "Cô An", past);
            var teacherA2 = Teacher.Create(centerA.Id, "GVA2", "Thầy Bình", past);
            var teacherA3 = Teacher.Create(centerA.Id, "GVA3", "Cô Chi", past);
            var teacherB = Teacher.Create(centerB.Id, "GVB", "Cô An (B)", past);
            teacherA.LinkAccount(teacherUser.Id);
            teacherB.LinkAccount(teacherUser.Id);
            // The same Google account studies in both centers; a hand-made student has no account.
            var studentA = Student.CreateForUser(centerA.Id, studentUser.Id, "HS-A1", "Lê Minh", past);
            var studentB = Student.CreateForUser(centerB.Id, studentUser.Id, "HS-B1", "Lê Minh", past);
            var manual = Student.Create(centerA.Id, "HS-M1", "Trần Nam", past);
            var enrolledAt = t0.AddDays(-10);
            var enrollA = Enrollment.Create(centerA.Id, a1.Id, studentA.Id, enrolledAt);
            var enrollB = Enrollment.Create(centerB.Id, b1.Id, studentB.Id, enrolledAt);
            var enrollM = Enrollment.Create(centerA.Id, a2.Id, manual.Id, enrolledAt);
            a1.Enroll(enrolledAt);
            b1.Enroll(enrolledAt);
            a2.Enroll(enrolledAt);
            TeachingSession Session(TeachingClass c, Teacher t, Room r, DateTimeOffset start, int minutes) =>
                TeachingSession.Create(c.CenterId, c.Id, t.Id, r.Id, start, start.AddMinutes(minutes), past);
            var beforeJoining = Session(a1, teacherA, roomA1, t0.AddDays(-12), 90);
            var s1 = Session(a1, teacherA, roomA1, t0.AddDays(2), 90);
            var s3 = Session(a1, teacherA, roomA1, t0.AddDays(12), 90);
            var soon = Session(a1, teacherA, roomA1, t0.AddHours(10), 60);
            var a2Clash = Session(a2, teacherA2, roomA2, t0.AddDays(2), 60);
            var b1Clash = Session(b1, teacherB, roomB1, t0.AddDays(2).AddMinutes(30), 60);
            await using (var academic = new AcademicsDbContext(academicOptions))
            {
                academic.Subjects.AddRange(subjectA, subjectB);
                academic.Classes.AddRange(a1, a2, a3, b1);
                academic.Teachers.AddRange(teacherA, teacherA2, teacherA3, teacherB);
                academic.Students.AddRange(studentA, studentB, manual);
                academic.Enrollments.AddRange(enrollA, enrollB, enrollM);
                academic.Sessions.AddRange(beforeJoining, s1, s3, soon, a2Clash, b1Clash);
                await academic.SaveChangesAsync();
            }

            // ---- Wave 1: personal schedule across centers ----
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var workflow = new PersonalScheduleWorkflow(new PersonalScheduleStore(db, new StudentTimetableStore(db)));
                Assert.False((await workflow.GetAsync(studentUser.Id, t0, t0.AddDays(43), default)).IsSuccess);
                var mine = (await workflow.GetAsync(studentUser.Id, t0.AddDays(-20), t0.AddDays(20), default)).Value;
                // Nothing before the student joined; both centers' classes are merged.
                Assert.DoesNotContain(mine.Entries, x => x.SessionId == beforeJoining.Id);
                Assert.Equal(new[] { soon.Id, s1.Id, b1Clash.Id, s3.Id }.Order(), mine.Entries.Select(x => x.SessionId).Order());
                Assert.All(mine.Entries, x => Assert.Equal("Student", x.Role));
                Assert.Equal(2, mine.Centers.Count);
                var first = mine.Entries.Single(x => x.SessionId == s1.Id);
                Assert.Equal($"Alpha {marker}", first.CenterName);
                Assert.Equal("Toán 9A", first.ClassName);
                Assert.Equal("Cô An", first.TeacherName);
                Assert.Equal("Phòng A101", first.RoomName);
                Assert.Equal([b1Clash.Id], first.OverlapsWith);

                var teaching = (await workflow.GetAsync(teacherUser.Id, t0, t0.AddDays(20), default)).Value;
                Assert.All(teaching.Entries, x => Assert.Equal("Teacher", x.Role));
                Assert.Contains(teaching.Entries, x => x.SessionId == b1Clash.Id && x.OverlapsWith.Contains(s1.Id));
            }

            ScheduleConflictChecker Checker(AcademicsDbContext db) => new(new StudentTimetableStore(db), clock);

            // Drag & drop preview: the teacher's own sessions at another center (same login) are counted, never named.
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var busy = await Checker(db).SessionImpactAsync(centerA.Id, a1.Id, s1.StartUtc, s1.EndUtc, s1.Id, s1.StartUtc,
                    default, teacherA.Id);
                Assert.Equal(1, busy.TeacherSessionsElsewhere);
                var free = await Checker(db).SessionImpactAsync(centerA.Id, a1.Id, s1.StartUtc, s1.EndUtc, s1.Id, s1.StartUtc,
                    default, teacherA2.Id);
                Assert.Equal(0, free.TeacherSessionsElsewhere);
                var later = await Checker(db).SessionImpactAsync(centerA.Id, a1.Id, s1.StartUtc.AddHours(3), s1.EndUtc.AddHours(3),
                    s1.Id, s1.StartUtc, default, teacherA.Id);
                Assert.Equal(0, later.TeacherSessionsElsewhere);
            }
            ScheduleChangeRecorder Recorder(AcademicsDbContext db) => new(
                new ScheduleChangeStore(db, clock, new ServiceCollection().BuildServiceProvider()),
                new StudentTimetableStore(db), Checker(db));

            // ---- Wave 2: conflicts when joining a class ----
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                // The student's own check names the other center; two picked classes are compared too.
                var check = await Checker(db).CheckApplicationAsync(centerA.Id, studentUser.Id, [a2.Id], default);
                var items = Assert.Single(check.Value.Classes).Conflicts;
                Assert.Contains(items, x => x.SameCenter && x.OtherClassName == "Toán 9A");
                Assert.Contains(items, x => !x.SameCenter && x.OtherCenterName == $"Beta {marker}");
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                // Manual enrollment into a class that meets with A2 is blocked until confirmed.
                var enrollments = new EnrollmentWorkflow(new TeachingStore(db), clock, null, Checker(db));
                var (blocked, conflicts) = await enrollments.EnrollAsync(centerA.Id, a1.Id, manual.Id, false, default);
                Assert.Equal(TeachingErrors.StudentScheduleConflict, blocked.Error);
                var clash = Assert.Single(conflicts!.SameCenterConflicts);
                Assert.Equal("A2", clash.OtherClassCode);
                Assert.Equal(0, conflicts.OtherCenterConflictCount);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var enrollments = new EnrollmentWorkflow(new TeachingStore(db), clock, null, Checker(db));
                var (confirmed, _) = await enrollments.EnrollAsync(centerA.Id, a1.Id, manual.Id, true, default);
                Assert.True(confirmed.IsSuccess);
            }
            Guid applicationId;
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var registration = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db), clock);
                var submitted = await registration.SubmitAsync(centerA.Id, studentUser.Id, "Lê Minh", "Lê Bố", "0912345678",
                    [a2.Id], Guid.NewGuid().ToString(), default);
                applicationId = Assert.Single(submitted.Value.Items).Id;
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var registration = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db), clock,
                    null, null, Checker(db));
                var (refused, check) = await registration.ApproveAsync(centerA.Id, applicationId, owner.Id, false, default);
                Assert.Equal(TeachingErrors.StudentScheduleConflict, refused.Error);
                Assert.Single(check!.SameCenterConflicts);
                // The center sees only how many sessions clash elsewhere, never the other center's name.
                Assert.Equal(1, check.OtherCenterConflictCount);
                Assert.All(check.SameCenterConflicts, x => Assert.Null(x.OtherCenterName));
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var registration = new ClassRegistrationWorkflow(new ClassRegistrationStore(db), new TeachingStore(db), clock,
                    null, null, Checker(db));
                var (approved, _) = await registration.ApproveAsync(centerA.Id, applicationId, owner.Id, true, default);
                Assert.True(approved.IsSuccess);
                Assert.Equal(1, approved.Value.OtherCenterConflictCount);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                // Moving s1 elsewhere: both A1 students also attend A2 at that time; one also has center B then.
                var impact = await Checker(db).SessionImpactAsync(centerA.Id, a1.Id, s1.StartUtc, s1.EndUtc, s1.Id,
                    s1.StartUtc, default);
                Assert.Equal(2, impact.AffectedStudentCount);
                Assert.False(impact.IsUrgent);
                Assert.Equal(2, impact.SameCenterConflicts.Count);
                Assert.Equal(1, impact.OtherCenterConflictStudentCount);
                Assert.Equal(1, impact.StudentsWithoutAccount);
            }

            // ---- Wave 3: change log, notices, email ----
            SessionScheduler Scheduler(AcademicsDbContext db) => new(new TeachingStore(db), clock, new TeachingNameReader(db), Recorder(db));
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var moved = await Scheduler(db).RescheduleAsync(centerA.Id, s3.Id, teacherA.Id, roomA1.Id,
                    s3.StartUtc.AddDays(1), s3.EndUtc.AddDays(1), 0, "Giáo viên đi tập huấn", owner.Id, default);
                Assert.True(moved.IsSuccess);
                var cancelled = await Scheduler(db).CancelAsync(centerA.Id, soon.Id, 0, "Mất điện", owner.Id, default);
                Assert.True(cancelled.IsSuccess);
            }
            Guid rescheduleNoticeId;
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var changes = await db.ScheduleChanges.AsNoTracking().Where(x => x.CenterId == centerA.Id).ToListAsync();
                var reschedule = changes.Single(x => x.Kind == ScheduleChangeKind.SessionRescheduled);
                Assert.Equal(s3.StartUtc, reschedule.OldStartUtc);
                Assert.Equal(s3.StartUtc.AddDays(1), reschedule.NewStartUtc);
                Assert.False(reschedule.IsUrgent);
                // A session within 24 hours is flagged urgent ("báo gấp").
                Assert.True(changes.Single(x => x.Kind == ScheduleChangeKind.SessionCancelled).IsUrgent);
                var notices = await db.ScheduleNotices.AsNoTracking().Where(x => x.ChangeId == reschedule.Id).ToListAsync();
                Assert.Equal(2, notices.Count);
                Assert.True(notices.Single(x => x.StudentId == studentA.Id).EmailQueued);
                Assert.False(notices.Single(x => x.StudentId == manual.Id).EmailQueued);
                rescheduleNoticeId = notices.Single(x => x.StudentId == studentA.Id).Id;

                var emails = await db.Database.SqlQueryRaw<string>(
                    "SELECT subject AS \"Value\" FROM iam.email_outbox_messages WHERE recipient = @r",
                    new NpgsqlParameter("r", studentEmail)).ToListAsync();
                Assert.Equal(2, emails.Count);
                Assert.Contains(emails, x => x.StartsWith("[Báo gấp]", StringComparison.Ordinal));

                // The schedule shows where the session moved from.
                var workflow = new PersonalScheduleWorkflow(new PersonalScheduleStore(db, new StudentTimetableStore(db)));
                var mine = (await workflow.GetAsync(studentUser.Id, t0, t0.AddDays(20), default)).Value;
                var movedEntry = mine.Entries.Single(x => x.SessionId == s3.Id);
                Assert.Equal(s3.StartUtc, movedEntry.PreviousStartUtc);
                Assert.Equal("Giáo viên đi tập huấn", movedEntry.RescheduleReason);
                var cancelledEntry = mine.Entries.Single(x => x.SessionId == soon.Id);
                Assert.Equal("Cancelled", cancelledEntry.Status);
                Assert.Equal("Mất điện", cancelledEntry.StatusReason);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var queries = new ScheduleChangeQueries(new ScheduleChangeStore(db, clock, new ServiceCollection().BuildServiceProvider()),
                    new StudentTimetableStore(db), new TeachingNameReader(db), clock);
                var page = await queries.MyNoticesAsync(studentUser.Id, false, 1, 20, default);
                Assert.Equal(2, page.UnreadCount);
                Assert.All(page.Items, x => Assert.Equal($"Alpha {marker}", x.CenterName));
                Assert.Equal("Acknowledged", (await queries.AcknowledgeAsync(studentUser.Id, page.Items.Single(x => x.Kind == "SessionCancelled").Id, default)).Value);
                Assert.False((await queries.AcknowledgeAsync(teacherUser.Id, rescheduleNoticeId, default)).IsSuccess);
            }

            // ---- Wave 3: recurring schedule notice period ----
            var a3Student = Enrollment.Create(centerA.Id, a3.Id, manual.Id, enrolledAt);
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                db.Enrollments.Add(a3Student);
                (await db.Classes.SingleAsync(x => x.Id == a3.Id)).Enroll(enrolledAt);
                await db.SaveChangesAsync();
            }
            Guid seriesId;
            var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t0.AddDays(3), CenterTime.Zone).DateTime);
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var created = await new RecurringScheduleWorkflow(new TeachingStore(db), clock).CreateAsync(centerA.Id,
                    new CreateScheduleSeriesRequest(Guid.NewGuid(), a3.Id, teacherA3.Id, roomA3.Id, null, start, start.AddDays(14),
                        new TimeOnly(6, 0), 60, [start.DayOfWeek.ToString()], null), default);
                Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
                seriesId = created.Value.Series.Id;
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = new RecurringScheduleWorkflow(new TeachingStore(db), clock, new TeachingNameReader(db), Recorder(db));
                Assert.Equal(TeachingErrors.InvalidSessionReason,
                    (await flow.StopAsync(centerA.Id, seriesId, start, 0, " ", true, owner.Id, default)).Error);
                // Less than 7 days before the first affected session: confirm first.
                Assert.Equal(ScheduleChangeErrors.ShortNotice,
                    (await flow.StopAsync(centerA.Id, seriesId, start, 0, "Lớp tạm nghỉ", false, owner.Id, default)).Error);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = new RecurringScheduleWorkflow(new TeachingStore(db), clock, new TeachingNameReader(db), Recorder(db));
                var stopped = await flow.StopAsync(centerA.Id, seriesId, start, 0, "Lớp tạm nghỉ", true, owner.Id, default);
                Assert.True(stopped.IsSuccess);
                var change = await db.ScheduleChanges.AsNoTracking().SingleAsync(x => x.SeriesId == seriesId);
                Assert.Equal(ScheduleChangeKind.SeriesStopped, change.Kind);
                Assert.True(change.IsUrgent);
                Assert.Equal(stopped.Value.SessionsCancelled, change.AffectedSessionCount);
                Assert.Single(await db.ScheduleNotices.AsNoTracking().Where(x => x.ChangeId == change.Id).ToListAsync());
                Assert.All(await db.Sessions.AsNoTracking().Where(x => x.SeriesId == seriesId).ToListAsync(),
                    x => Assert.Equal("Lớp tạm nghỉ", x.CancellationReason));
            }

            // ---- Wave 3: student requests ----
            StudentRequestWorkflow Requests(AcademicsDbContext db) => new(new StudentRequestStore(db), new TeachingStore(db),
                new StudentTimetableStore(db), Checker(db), clock);
            StudentRequestDto absence;
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var flow = Requests(db);
                Assert.Equal(StudentRequestErrors.InvalidSession, (await flow.SubmitAsync(centerA.Id, studentUser.Id,
                    new("ExcusedAbsence", a1.Id, beforeJoining.Id, null, null, "Ốm"), default)).Error);
                Assert.Equal(StudentRequestErrors.NotEnrolled, (await flow.SubmitAsync(centerA.Id, studentUser.Id,
                    new("Withdrawal", a3.Id, null, null, null, "Bận"), default)).Error);
                Assert.Equal(StudentRequestErrors.Invalid, (await flow.SubmitAsync(centerA.Id, studentUser.Id,
                    new("ExcusedAbsence", a1.Id, null, null, null, "Thiếu buổi"), default)).Error);
                var sent = await flow.SubmitAsync(centerA.Id, studentUser.Id,
                    new("ExcusedAbsence", a1.Id, s3.Id, null, rescheduleNoticeId, "Trùng lịch thi"), default);
                Assert.True(sent.IsSuccess);
                absence = sent.Value;
                Assert.Equal("Toán 9A", absence.ClassName);
                Assert.Equal(s3.StartUtc.AddDays(1), absence.SessionStartUtc);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                Assert.Equal(StudentRequestErrors.Duplicate, (await Requests(db).SubmitAsync(centerA.Id, studentUser.Id,
                    new("ExcusedAbsence", a1.Id, s3.Id, null, null, "Lần nữa"), default)).Error);
                Assert.Equal(NoticeResponse.Requested,
                    (await db.ScheduleNotices.AsNoTracking().SingleAsync(x => x.Id == rescheduleNoticeId)).Response);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var (approved, _) = await Requests(db).ApproveAsync(centerA.Id, absence.Id, owner.Id, "Đồng ý", 0, false, default);
                Assert.True(approved.IsSuccess);
                Assert.Equal("Approved", approved.Value.Status);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                // The attendance sheet shows "Vắng có phép" for that student before anyone marks it.
                var sheet = await new AttendanceWorkflow(new TeachingStore(db), new AttendanceStore(db), clock)
                    .GetAsync(centerA.Id, s3.Id, default);
                var row = sheet.Value.Students.Single(x => x.StudentId == studentA.Id);
                Assert.Equal("ExcusedAbsent", row.Mark);
                Assert.True(row.ExcusedAbsenceApproved);
                Assert.Equal("Unknown", sheet.Value.Students.Single(x => x.StudentId == manual.Id).Mark);

                var queries = new ScheduleChangeQueries(new ScheduleChangeStore(db, clock, new ServiceCollection().BuildServiceProvider()),
                    new StudentTimetableStore(db), new TeachingNameReader(db), clock);
                var changes = await queries.CenterChangesAsync(centerA.Id, null, 1, 20, default);
                var responses = changes.Items.Single(x => x.Kind == "SessionRescheduled").Responses!;
                Assert.Equal(2, responses.Notified);
                Assert.Equal(1, responses.WithoutAccount);
                Assert.Equal(1, responses.RequestsByKind["ExcusedAbsence"]);
                Assert.Equal("Toán 9A", changes.Items.Single(x => x.Kind == "SessionRescheduled").ClassName);
                Assert.Equal("Toán 9C", changes.Items.Single(x => x.Kind == "SeriesStopped").ClassName);
            }
            StudentRequestDto transfer, withdrawal, makeup;
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                transfer = (await Requests(db).SubmitAsync(centerA.Id, studentUser.Id,
                    new("Transfer", a1.Id, null, a3.Id, null, "Lịch mới hợp hơn"), default)).Value;
                withdrawal = (await Requests(db).SubmitAsync(centerA.Id, studentUser.Id,
                    new("Withdrawal", a2.Id, null, null, null, "Không theo kịp"), default)).Value;
                makeup = (await Requests(db).SubmitAsync(centerA.Id, studentUser.Id,
                    new("Makeup", a1.Id, soon.Id, null, null, "Xin học bù buổi mất điện"), default)).Value;
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var (moved, _) = await Requests(db).ApproveAsync(centerA.Id, transfer.Id, owner.Id, null, 0, false, default);
                Assert.True(moved.IsSuccess);
                var (left, _) = await Requests(db).ApproveAsync(centerA.Id, withdrawal.Id, owner.Id, null, 0, false, default);
                Assert.True(left.IsSuccess);
                Assert.Equal(StudentRequestErrors.ConcurrentChange,
                    (await Requests(db).RejectAsync(centerA.Id, makeup.Id, owner.Id, "Không còn lớp ghép", 5, default)).Error);
                var rejected = await Requests(db).RejectAsync(centerA.Id, makeup.Id, owner.Id, "Không còn lớp ghép", 0, default);
                Assert.Equal("Rejected", rejected.Value.Status);
            }
            await using (var db = new AcademicsDbContext(academicOptions))
            {
                var active = await db.Enrollments.AsNoTracking().Where(x => x.StudentId == studentA.Id).ToListAsync();
                Assert.Equal([a3.Id], active.Where(x => x.WithdrawnAtUtc == null).Select(x => x.ClassId));
                Assert.StartsWith("Chuyển sang lớp Toán 9C", active.Single(x => x.ClassId == a1.Id).WithdrawalReason);
                Assert.StartsWith("Học sinh xin rút", active.Single(x => x.ClassId == a2.Id).WithdrawalReason);
                Assert.Equal(1, (await db.Classes.AsNoTracking().SingleAsync(x => x.Id == a1.Id)).EnrolledCount);
                var mine = await Requests(db).MineAsync(studentUser.Id, 1, 20, default);
                Assert.Equal(4, mine.TotalCount);
                var pending = await Requests(db).ListAsync(centerA.Id, StudentRequestStatus.Pending, null, null, 1, 20, default);
                Assert.Equal(0, pending.TotalCount);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM academics.student_requests WHERE center_id IN (@a, @b);
                DELETE FROM academics.schedule_notices WHERE center_id IN (@a, @b);
                DELETE FROM academics.schedule_changes WHERE center_id IN (@a, @b);
                DELETE FROM academics.attendance_entries WHERE center_id IN (@a, @b);
                DELETE FROM academics.attendance_revisions WHERE center_id IN (@a, @b);
                DELETE FROM academics.attendance_sheets WHERE center_id IN (@a, @b);
                DELETE FROM academics.class_applications WHERE center_id IN (@a, @b);
                DELETE FROM academics.enrollments WHERE center_id IN (@a, @b);
                DELETE FROM academics.teaching_sessions WHERE center_id IN (@a, @b);
                DELETE FROM academics.schedule_series WHERE center_id IN (@a, @b);
                DELETE FROM academics.classes WHERE center_id IN (@a, @b);
                DELETE FROM academics.students WHERE center_id IN (@a, @b);
                DELETE FROM academics.student_code_counters WHERE center_id IN (@a, @b);
                DELETE FROM academics.teachers WHERE center_id IN (@a, @b);
                DELETE FROM academics.subjects WHERE center_id IN (@a, @b);
                DELETE FROM organization.rooms WHERE center_id IN (@a, @b);
                DELETE FROM organization.center_status_history WHERE center_id IN (@a, @b);
                DELETE FROM organization.centers WHERE id IN (@a, @b);
                DELETE FROM iam.email_outbox_messages WHERE recipient = @email;
                DELETE FROM iam.users WHERE id IN (@owner, @student, @teacher);
                """, connection);
            cleanup.Parameters.AddWithValue("a", centerA.Id);
            cleanup.Parameters.AddWithValue("b", centerB.Id);
            cleanup.Parameters.AddWithValue("email", studentEmail);
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("student", studentUser.Id);
            cleanup.Parameters.AddWithValue("teacher", teacherUser.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
