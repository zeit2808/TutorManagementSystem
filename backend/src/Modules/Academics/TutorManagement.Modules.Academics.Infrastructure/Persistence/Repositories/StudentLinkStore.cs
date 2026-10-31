using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

/// <summary>M3 persistence: activation links, class invite links, join audit and profile merges.</summary>
public sealed class StudentLinkStore(AcademicsDbContext db) : IStudentLinkStore
{
    public Task<Student?> FindStudentAsync(Guid centerId, Guid studentId, CancellationToken token) =>
        db.Students.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == studentId, token);

    public Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken token) =>
        db.Students.SingleOrDefaultAsync(x => x.CenterId == centerId && x.UserId == userId, token);

    public Task<Student?> FindUnlinkedNamesakeAsync(Guid centerId, string fullName, Guid exceptId, CancellationToken token) =>
        db.Students.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.Id != exceptId && x.UserId == null && x.ArchivedAtUtc == null &&
                EF.Functions.ILike(x.FullName, Escape(fullName), "\\"))
            .OrderBy(x => x.Code).FirstOrDefaultAsync(token);

    public async Task<IReadOnlyList<Student>> ListUnlinkedEnrolledAsync(Guid centerId, Guid classId, CancellationToken token) =>
        await (from enrollment in db.Enrollments.AsNoTracking()
               join student in db.Students.AsNoTracking()
                   on new { enrollment.CenterId, Id = enrollment.StudentId } equals new { student.CenterId, student.Id }
               where enrollment.CenterId == centerId && enrollment.ClassId == classId &&
                     enrollment.WithdrawnAtUtc == null && student.UserId == null && student.ArchivedAtUtc == null
               select student).ToArrayAsync(token);

    public async Task<IReadOnlyList<StudentClassName>> ListActiveClassNamesAsync(Guid centerId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken token)
    {
        var ids = studentIds.ToArray();
        return await (from enrollment in db.Enrollments.AsNoTracking()
                      join cls in db.Classes.AsNoTracking()
                          on new { enrollment.CenterId, Id = enrollment.ClassId } equals new { cls.CenterId, cls.Id }
                      where enrollment.CenterId == centerId && ids.Contains(enrollment.StudentId) &&
                            enrollment.WithdrawnAtUtc == null
                      orderby cls.Name
                      select new StudentClassName(enrollment.StudentId, cls.Name)).ToArrayAsync(token);
    }

    public async Task<IReadOnlyList<StudentActivationLink>> ListActiveActivationLinksAsync(Guid centerId, Guid studentId,
        CancellationToken token) =>
        await db.StudentActivationLinks.Where(x => x.CenterId == centerId && x.StudentId == studentId &&
            x.UsedAtUtc == null && x.RevokedAtUtc == null).ToArrayAsync(token);

    public Task<StudentActivationLink?> FindActivationLinkAsync(string tokenHash, CancellationToken token) =>
        db.StudentActivationLinks.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, token);

    public async Task<IReadOnlyDictionary<Guid, StudentActivationLink>> LatestActivationLinksAsync(Guid centerId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken token)
    {
        var ids = studentIds.ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, StudentActivationLink>();
        var rows = await db.StudentActivationLinks.AsNoTracking()
            .Where(x => x.CenterId == centerId && ids.Contains(x.StudentId)).ToArrayAsync(token);
        return rows.GroupBy(x => x.StudentId)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.CreatedAtUtc).First());
    }

    public async Task<IReadOnlyList<ClassInviteLink>> ListInvitesAsync(Guid centerId, Guid classId, CancellationToken token) =>
        await db.ClassInviteLinks.AsNoTracking().Where(x => x.CenterId == centerId && x.ClassId == classId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50).ToArrayAsync(token);

    public Task<ClassInviteLink?> FindInviteAsync(Guid centerId, Guid inviteId, CancellationToken token) =>
        db.ClassInviteLinks.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == inviteId, token);

    public Task<ClassInviteLink?> FindInviteByTokenAsync(string token, CancellationToken token2) =>
        string.IsNullOrWhiteSpace(token) || token.Length > 64
            ? Task.FromResult<ClassInviteLink?>(null)
            : db.ClassInviteLinks.SingleOrDefaultAsync(x => x.Token == token.Trim(), token2);

    public Task<ClassInviteAttempt?> FindAttemptAsync(Guid linkId, Guid userId, CancellationToken token) =>
        db.ClassInviteAttempts.SingleOrDefaultAsync(x => x.LinkId == linkId && x.UserId == userId, token);

    public Task<InviteClassRow?> FindInviteClassAsync(Guid centerId, Guid classId, CancellationToken token) =>
        (from cls in db.Classes.AsNoTracking()
         join subject in db.Subjects.AsNoTracking()
             on new { cls.CenterId, Id = cls.SubjectId } equals new { subject.CenterId, subject.Id }
         where cls.CenterId == centerId && cls.Id == classId
         select new InviteClassRow(cls.Id, cls.CenterId, cls.Name, subject.Name, cls.MaxStudents - cls.EnrolledCount,
             cls.ArchivedAtUtc == null)).SingleOrDefaultAsync(token);

    public async Task<IReadOnlyList<ClassInviteJoin>> ListJoinsAsync(Guid centerId, Guid? classId, int take,
        CancellationToken token) =>
        await db.ClassInviteJoins.AsNoTracking()
            .Where(x => x.CenterId == centerId && (classId == null || x.ClassId == classId))
            .OrderByDescending(x => x.JoinedAtUtc).Take(take).ToArrayAsync(token);

    public Task<bool> CenterApprovedAsync(Guid centerId, CancellationToken token) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS(SELECT 1 FROM organization.centers WHERE id = @center AND status = 'Approved') AS \"Value\"",
            new NpgsqlParameter("center", centerId)).SingleAsync(token);

    public Task<bool> HasActiveEnrollmentAsync(Guid centerId, Guid classId, Guid studentId, CancellationToken token) =>
        db.Enrollments.AnyAsync(x => x.CenterId == centerId && x.ClassId == classId && x.StudentId == studentId &&
            x.WithdrawnAtUtc == null, token);

    public async Task<ITeachingGuard> BeginGuardAsync(Guid centerId, Guid? userId, Guid? classId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken token)
    {
        var keys = new List<string>();
        if (userId is { } user) keys.Add($"application-user:{centerId}:{user}");
        if (classId is { } cls) keys.Add($"class:{centerId}:{cls}");
        keys.AddRange(studentIds.Select(id => $"student:{centerId}:{id}"));
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new Guard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> SharedActiveClassNamesAsync(Guid centerId, Guid a, Guid b,
        CancellationToken token) =>
        await (from first in db.Enrollments.AsNoTracking()
               join second in db.Enrollments.AsNoTracking() on first.ClassId equals second.ClassId
               join cls in db.Classes.AsNoTracking()
                   on new { first.CenterId, Id = first.ClassId } equals new { cls.CenterId, cls.Id }
               where first.CenterId == centerId && first.StudentId == a && second.StudentId == b &&
                     first.WithdrawnAtUtc == null && second.WithdrawnAtUtc == null
               select cls.Name).ToArrayAsync(token);

    public Task<bool> SharedAttendanceAsync(Guid centerId, Guid a, Guid b, CancellationToken token) =>
        (from first in db.AttendanceEntries.AsNoTracking()
         join second in db.AttendanceEntries.AsNoTracking() on first.SessionId equals second.SessionId
         where first.CenterId == centerId && first.StudentId == a && second.StudentId == b
         select first.Id).AnyAsync(token);

    public async Task<(int Enrollments, int Attendance)> MoveStudentDataAsync(Guid centerId, Guid sourceId,
        Guid targetId, CancellationToken token)
    {
        // Attendance rows reference (enrollment, student): both move in one statement so the composite key holds.
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand("""
            WITH e AS (
                UPDATE academics.enrollments SET student_id = @target
                WHERE center_id = @center AND student_id = @source RETURNING 1),
            a AS (
                UPDATE academics.attendance_entries SET student_id = @target
                WHERE center_id = @center AND student_id = @source RETURNING 1)
            SELECT (SELECT count(*) FROM e)::int, (SELECT count(*) FROM a)::int
            """, connection, (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction());
        command.Parameters.AddRange(Params(centerId, sourceId, targetId));
        var moved = (Enrollments: 0, Attendance: 0);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (await reader.ReadAsync(token)) moved = (reader.GetInt32(0), reader.GetInt32(1));
        }
        // Notices and requests that would collide with the target's own rows stay on the archived source (history).
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE academics.schedule_notices n SET student_id = @target
            WHERE n.center_id = @center AND n.student_id = @source
              AND NOT EXISTS (SELECT 1 FROM academics.schedule_notices t
                              WHERE t.change_id = n.change_id AND t.student_id = @target);
            UPDATE academics.student_requests r SET student_id = @target
            WHERE r.center_id = @center AND r.student_id = @source
              AND (r.status <> 'Pending' OR NOT EXISTS (
                   SELECT 1 FROM academics.student_requests t
                   WHERE t.student_id = @target AND t.status = 'Pending' AND t.kind = r.kind
                     AND (t.session_id = r.session_id OR (r.session_id IS NULL AND t.class_id = r.class_id))));
            UPDATE academics.class_invite_joins SET student_id = @target
            WHERE center_id = @center AND student_id = @source;
            """, Params(centerId, sourceId, targetId), token);
        return (moved.Enrollments, moved.Attendance);
    }

    public async Task AddAsync<T>(T entity, CancellationToken token) where T : class => await db.AddAsync(entity, token);

    public Task SaveAsync(CancellationToken token) => db.SaveChangesAsync(token);

    /// <summary>An ILIKE pattern that matches the text exactly, ignoring case.</summary>
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static NpgsqlParameter[] Params(Guid centerId, Guid sourceId, Guid targetId) =>
    [
        new("center", centerId), new("source", sourceId), new("target", targetId)
    ];

    private sealed class Guard(IDbContextTransaction transaction) : ITeachingGuard
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
