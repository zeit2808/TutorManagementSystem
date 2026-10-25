using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

/// <summary>Cross-center reads of who is busy when. Center and room names come from the Organizations schema via SQL.</summary>
public sealed class StudentTimetableStore(AcademicsDbContext db) : IStudentTimetableStore
{
    public async Task<IReadOnlyList<BusySlot>> ListBusyAsync(IReadOnlyCollection<Guid> studentIds,
        IReadOnlyCollection<Guid> userIds, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token)
    {
        var students = studentIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        var users = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (students.Length == 0 && users.Length == 0) return [];
        return await (from session in db.Sessions.AsNoTracking()
                      join enrollment in db.Enrollments.AsNoTracking() on session.ClassId equals enrollment.ClassId
                      join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
                      where session.CancelledAtUtc == null && session.NotHeldAtUtc == null &&
                            session.StartUtc < toUtc && session.EndUtc > fromUtc &&
                            session.StartUtc >= enrollment.EnrolledAtUtc &&
                            (enrollment.WithdrawnAtUtc == null || session.StartUtc < enrollment.WithdrawnAtUtc) &&
                            (students.Contains(student.Id) || (student.UserId != null && users.Contains(student.UserId.Value)))
                      orderby session.StartUtc
                      select new BusySlot(session.Id, session.CenterId, session.ClassId, student.Id, student.UserId,
                          session.StartUtc, session.EndUtc)).ToArrayAsync(token);
    }

    public async Task<IReadOnlyList<CandidateSlot>> ListClassSessionsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token)
    {
        var ids = classIds.Distinct().ToArray();
        return await db.Sessions.AsNoTracking()
            .Where(x => x.CenterId == centerId && ids.Contains(x.ClassId) && x.CancelledAtUtc == null &&
                x.NotHeldAtUtc == null && x.StartUtc >= fromUtc && x.StartUtc < toUtc)
            .OrderBy(x => x.StartUtc)
            .Select(x => new CandidateSlot(x.Id, x.ClassId, x.StartUtc, x.EndUtc)).ToArrayAsync(token);
    }

    public async Task<IReadOnlyList<RosterStudent>> ListRosterAtAsync(Guid centerId, Guid classId, DateTimeOffset instantUtc,
        CancellationToken token) =>
        await (from enrollment in db.Enrollments.AsNoTracking()
               join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
               where enrollment.CenterId == centerId && enrollment.ClassId == classId &&
                     enrollment.EnrolledAtUtc <= instantUtc &&
                     (enrollment.WithdrawnAtUtc == null || enrollment.WithdrawnAtUtc > instantUtc)
               orderby student.FullName, student.Id
               select new RosterStudent(student.Id, student.UserId, student.FullName, student.Code)).ToArrayAsync(token);

    public async Task<IReadOnlyList<ClassLabelRow>> ReadClassLabelsAsync(IReadOnlyCollection<Guid> classIds, CancellationToken token)
    {
        var ids = classIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        return await db.Classes.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new ClassLabelRow(x.CenterId, x.Id, x.Code, x.Name)).ToArrayAsync(token);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ReadCenterNamesAsync(IReadOnlyCollection<Guid> centerIds,
        CancellationToken token)
    {
        var ids = centerIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        var rows = await db.Database.SqlQueryRaw<NameRow>(
                "SELECT id AS \"Id\", display_name AS \"Name\" FROM organization.centers WHERE id = ANY(@ids)",
                new NpgsqlParameter("ids", ids)).ToListAsync(token);
        return rows.ToDictionary(x => x.Id, x => x.Name);
    }

    public Task<int> CountTeacherSessionsElsewhereAsync(Guid centerId, Guid teacherId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, CancellationToken token) =>
        db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value"
            FROM academics.teaching_sessions s
            JOIN academics.teachers t ON t.center_id = s.center_id AND t.id = s.teacher_id
            WHERE t.user_id = (SELECT me.user_id FROM academics.teachers me WHERE me.center_id = @center AND me.id = @teacher)
              AND s.center_id <> @center AND s.cancelled_at_utc IS NULL AND s.not_held_at_utc IS NULL
              AND s.start_utc < @end AND s.end_utc > @start
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("teacher", teacherId),
            new NpgsqlParameter("start", startUtc), new NpgsqlParameter("end", endUtc)).FirstAsync(token);

    internal static async Task<Dictionary<Guid, string>> ReadRoomNamesAsync(AcademicsDbContext db,
        IReadOnlyCollection<Guid> roomIds, CancellationToken token)
    {
        var ids = roomIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var rows = await db.Database.SqlQueryRaw<NameRow>(
                "SELECT id AS \"Id\", name AS \"Name\" FROM organization.rooms WHERE id = ANY(@ids)",
                new NpgsqlParameter("ids", ids)).ToListAsync(token);
        return rows.ToDictionary(x => x.Id, x => x.Name);
    }

    internal sealed class NameRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}

/// <summary>B4: one login's sessions across every center, as a student and as a teacher.</summary>
public sealed class PersonalScheduleStore(AcademicsDbContext db, IStudentTimetableStore timetable) : IPersonalScheduleStore
{
    public async Task<IReadOnlyList<PersonalSessionRow>> ListAsync(Guid userId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken token)
    {
        var learning = await (from session in db.Sessions.AsNoTracking()
                              join enrollment in db.Enrollments.AsNoTracking() on session.ClassId equals enrollment.ClassId
                              join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
                              where student.UserId == userId && session.StartUtc < toUtc && session.EndUtc > fromUtc &&
                                    session.StartUtc >= enrollment.EnrolledAtUtc &&
                                    (enrollment.WithdrawnAtUtc == null || session.StartUtc < enrollment.WithdrawnAtUtc)
                              select session).ToArrayAsync(token);
        var teaching = await (from session in db.Sessions.AsNoTracking()
                              join teacher in db.Teachers.AsNoTracking() on session.TeacherId equals teacher.Id
                              where teacher.UserId == userId && session.StartUtc < toUtc && session.EndUtc > fromUtc
                              select session).ToArrayAsync(token);
        return learning.Select(x => new PersonalSessionRow(x, "Student"))
            .Concat(teaching.Select(x => new PersonalSessionRow(x, "Teacher"))).ToArray();
    }

    public async Task<PersonalScheduleLookups> ReadLookupsAsync(Guid userId, IReadOnlyList<TeachingSession> sessions,
        CancellationToken token)
    {
        var classIds = sessions.Select(x => x.ClassId).Distinct().ToArray();
        var teacherIds = sessions.Select(x => x.TeacherId).Distinct().ToArray();
        var sessionIds = sessions.Select(x => x.Id).ToArray();
        var centers = await timetable.ReadCenterNamesAsync(sessions.Select(x => x.CenterId).Distinct().ToArray(), token);
        var classes = (await timetable.ReadClassLabelsAsync(classIds, token))
            .ToDictionary(x => x.ClassId, x => new ClassLabel(x.Code, x.Name));
        var teachers = await db.Teachers.AsNoTracking().Where(x => teacherIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName, token);
        var rooms = await StudentTimetableStore.ReadRoomNamesAsync(db, sessions.Select(x => x.RoomId).ToArray(), token);
        var moved = await db.ScheduleChanges.AsNoTracking()
            .Where(x => x.SessionId != null && sessionIds.Contains(x.SessionId.Value) &&
                x.Kind == ScheduleChangeKind.SessionRescheduled && x.OldStartUtc != null && x.OldEndUtc != null)
            .Select(x => new { SessionId = x.SessionId!.Value, x.OldStartUtc, x.OldEndUtc, x.CreatedAtUtc })
            .ToListAsync(token);
        var previous = moved.GroupBy(x => x.SessionId).ToDictionary(x => x.Key, x =>
        {
            var latest = x.OrderByDescending(r => r.CreatedAtUtc).First();
            return (latest.OldStartUtc!.Value, latest.OldEndUtc!.Value);
        });
        var absences = await db.StudentRequests.AsNoTracking()
            .Where(x => x.UserId == userId && x.Kind == StudentRequestKind.ExcusedAbsence && x.SessionId != null &&
                sessionIds.Contains(x.SessionId.Value) &&
                (x.Status == StudentRequestStatus.Pending || x.Status == StudentRequestStatus.Approved))
            .Select(x => new { SessionId = x.SessionId!.Value, x.Status }).ToListAsync(token);
        var absenceMap = absences.GroupBy(x => x.SessionId).ToDictionary(x => x.Key,
            x => x.Any(r => r.Status == StudentRequestStatus.Approved) ? StudentRequestStatus.Approved : StudentRequestStatus.Pending);
        return new PersonalScheduleLookups(centers, classes, teachers, rooms, previous, absenceMap);
    }
}
