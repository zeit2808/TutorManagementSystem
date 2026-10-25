using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class TeachingStore(AcademicsDbContext db) : ITeachingStore
{
    public Task<bool> SubjectIsActiveAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
        db.Subjects.AnyAsync(x => x.CenterId == centerId && x.Id == subjectId && x.ArchivedAtUtc == null, token);

    public Task<bool> RoomIsActiveAsync(Guid centerId, Guid roomId, CancellationToken token) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS(SELECT 1 FROM organization.rooms WHERE center_id = @center AND id = @room AND archived_at_utc IS NULL) AS \"Value\"",
            new NpgsqlParameter("center", centerId), new NpgsqlParameter("room", roomId)).FirstAsync(token);

    public Task<TeachingClass?> FindClassAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.Classes.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public async Task<(IReadOnlyList<TeachingClass> Items, int TotalCount)> ListClassesAsync(Guid centerId,
        bool includeArchived, string? search, int page, int pageSize, CancellationToken token)
    {
        var query = db.Classes.AsNoTracking().Where(x => x.CenterId == centerId &&
            (includeArchived || x.ArchivedAtUtc == null));
        if (Pattern(search) is { } pattern)
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.Name, pattern, "\\"));
        var count = await query.CountAsync(token);
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (items, count);
    }
    public Task<bool> ClassCodeExistsAsync(Guid centerId, string code, CancellationToken token) =>
        db.Classes.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);

    public Task<Student?> FindStudentAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.Students.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public async Task<(IReadOnlyList<Student> Items, int TotalCount)> ListStudentsAsync(Guid centerId,
        bool includeArchived, string? search, int page, int pageSize, CancellationToken token)
    {
        var query = db.Students.AsNoTracking().Where(x => x.CenterId == centerId &&
            (includeArchived || x.ArchivedAtUtc == null));
        if (Pattern(search) is { } pattern)
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.FullName, pattern, "\\") ||
                (x.ParentPhone != null && EF.Functions.ILike(x.ParentPhone, pattern, "\\")));
        var count = await query.CountAsync(token);
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (items, count);
    }
    public Task<bool> StudentCodeExistsAsync(Guid centerId, string code, CancellationToken token) =>
        db.Students.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);

    public Task<Teacher?> FindTeacherAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.Teachers.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public async Task<(IReadOnlyList<Teacher> Items, int TotalCount)> ListTeachersAsync(Guid centerId,
        bool includeArchived, string? search, int page, int pageSize, CancellationToken token)
    {
        var query = db.Teachers.AsNoTracking().Where(x => x.CenterId == centerId &&
            (includeArchived || x.ArchivedAtUtc == null));
        if (Pattern(search) is { } pattern)
            query = query.Where(x => EF.Functions.ILike(x.Code, pattern, "\\") || EF.Functions.ILike(x.FullName, pattern, "\\"));
        var count = await query.CountAsync(token);
        var items = await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (items, count);
    }
    public Task<bool> TeacherCodeExistsAsync(Guid centerId, string code, CancellationToken token) =>
        db.Teachers.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);
    public Task<Teacher?> FindTeacherByUserAsync(Guid centerId, Guid userId, CancellationToken token) =>
        db.Teachers.SingleOrDefaultAsync(x => x.CenterId == centerId && x.UserId == userId, token);
    public async Task<IReadOnlyList<TeachingSession>> ListTeacherSessionsAsync(Guid centerId, Guid teacherId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token) =>
        await db.Sessions.AsNoTracking().Where(x => x.CenterId == centerId && x.TeacherId == teacherId &&
            x.StartUtc < toUtc && x.EndUtc > fromUtc).OrderBy(x => x.StartUtc).ThenBy(x => x.Id).ToArrayAsync(token);

    public Task<Enrollment?> FindActiveEnrollmentAsync(Guid centerId, Guid classId, Guid studentId, CancellationToken token) =>
        db.Enrollments.SingleOrDefaultAsync(x => x.CenterId == centerId && x.ClassId == classId &&
            x.StudentId == studentId && x.WithdrawnAtUtc == null, token);
    public async Task<(IReadOnlyList<ClassStudentDto> Items, int TotalCount)> ListClassStudentsAsync(
        Guid centerId, Guid classId, EnrollmentFilter status, int page, int pageSize, CancellationToken token)
    {
        var query = from enrollment in db.Enrollments.AsNoTracking()
                    join student in db.Students.AsNoTracking()
                        on new { enrollment.CenterId, Id = enrollment.StudentId }
                        equals new { student.CenterId, student.Id }
                    where enrollment.CenterId == centerId && enrollment.ClassId == classId
                    select new { Enrollment = enrollment, Student = student };
        if (status == EnrollmentFilter.Active)
            query = query.Where(x => x.Enrollment.WithdrawnAtUtc == null);
        else if (status == EnrollmentFilter.Withdrawn)
            query = query.Where(x => x.Enrollment.WithdrawnAtUtc != null);

        var totalCount = await query.CountAsync(token);
        var items = await query.OrderBy(x => x.Student.FullName).ThenBy(x => x.Enrollment.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new ClassStudentDto
            {
                EnrollmentId = x.Enrollment.Id,
                StudentId = x.Student.Id,
                StudentCode = x.Student.Code,
                FullName = x.Student.FullName,
                Status = x.Enrollment.WithdrawnAtUtc == null ? "Active" : "Withdrawn",
                EnrolledAtUtc = x.Enrollment.EnrolledAtUtc,
                WithdrawnAtUtc = x.Enrollment.WithdrawnAtUtc,
                WithdrawalReason = x.Enrollment.WithdrawalReason,
                WithdrawnByUserId = x.Enrollment.WithdrawnByUserId
            }).ToArrayAsync(token);
        return (items, totalCount);
    }
    public Task<bool> StudentHasActiveEnrollmentAsync(Guid centerId, Guid studentId, CancellationToken token) =>
        db.Enrollments.AnyAsync(x => x.CenterId == centerId && x.StudentId == studentId && x.WithdrawnAtUtc == null, token);

    public Task<TeachingSession?> FindSessionAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.Sessions.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public Task<TeachingSession?> FindSessionSnapshotAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public Task<ScheduleSeries?> FindSeriesAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.ScheduleSeries.AsNoTracking().SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public Task<ScheduleSeries?> FindSeriesForUpdateAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.ScheduleSeries.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);
    public Task<ScheduleSeries?> FindSeriesByRequestIdAsync(Guid centerId, Guid requestId, CancellationToken token) =>
        db.ScheduleSeries.AsNoTracking().SingleOrDefaultAsync(x => x.CenterId == centerId && x.RequestId == requestId, token);
    public async Task<(IReadOnlyList<ScheduleSeries> Items, int TotalCount)> ListSeriesAsync(
        Guid centerId, Guid? classId, int page, int pageSize, CancellationToken token)
    {
        var query = db.ScheduleSeries.AsNoTracking().Where(x => x.CenterId == centerId &&
            (!classId.HasValue || x.ClassId == classId.Value));
        var totalCount = await query.CountAsync(token);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (items, totalCount);
    }
    public async Task<IReadOnlyList<TeachingSession>> ListSeriesSessionsAsync(Guid centerId,
        Guid seriesId, DateOnly fromDate, CancellationToken token) =>
        await db.Sessions.Where(x => x.CenterId == centerId && x.SeriesId == seriesId &&
            x.OccurrenceDate >= fromDate).OrderBy(x => x.StartUtc).ToArrayAsync(token);
    public async Task<IReadOnlyList<TeachingSession>> ListResourceSessionsAsync(Guid centerId,
        Guid classId, Guid teacherId, Guid roomId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken token) =>
        await db.Sessions.AsNoTracking().Where(x => x.CenterId == centerId &&
            x.CancelledAtUtc == null && x.NotHeldAtUtc == null && x.StartUtc < toUtc && x.EndUtc > fromUtc &&
            (x.ClassId == classId || x.TeacherId == teacherId || x.RoomId == roomId))
            .OrderBy(x => x.StartUtc).ToArrayAsync(token);
    public async Task<IReadOnlyList<TeachingSession>> ListSessionsAsync(Guid centerId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, SessionFilter filter, CancellationToken token) =>
        await db.Sessions.AsNoTracking().Where(x => x.CenterId == centerId && x.StartUtc < toUtc && x.EndUtc > fromUtc &&
                (filter.ClassId == null || x.ClassId == filter.ClassId) &&
                (filter.TeacherId == null || x.TeacherId == filter.TeacherId) &&
                (filter.RoomId == null || x.RoomId == filter.RoomId))
            .OrderBy(x => x.StartUtc).ThenBy(x => x.Id).ToArrayAsync(token);

    /// <summary>
    /// Decision D9: automatic codes are HS-000001, HS-000002… per center. A counter row hands out
    /// numbers atomically; numbers already taken by hand-typed codes are skipped.
    /// </summary>
    public async Task<string> NextStudentCodeAsync(Guid centerId, CancellationToken token)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var next = await db.Database.SqlQueryRaw<int>(
                "INSERT INTO academics.student_code_counters (center_id, last_value) VALUES (@center, 1) " +
                "ON CONFLICT (center_id) DO UPDATE SET last_value = academics.student_code_counters.last_value + 1 " +
                "RETURNING last_value AS \"Value\"",
                new NpgsqlParameter("center", centerId)).ToListAsync(token);
            var code = StudentCode.Format(next.Single());
            if (!await db.Students.AnyAsync(x => x.CenterId == centerId && x.Code == code, token)) return code;
        }
        throw new InvalidOperationException("Could not allocate a free student code.");
    }

    private static string? Pattern(string? search)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term)) return null;
        var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }
    public Task<bool> ClassHasFutureSessionsAsync(Guid centerId, Guid classId, DateTimeOffset now, CancellationToken token) =>
        db.Sessions.AnyAsync(x => x.CenterId == centerId && x.ClassId == classId &&
            x.CancelledAtUtc == null && x.NotHeldAtUtc == null && x.EndUtc > now, token);
    public Task<bool> TeacherHasFutureSessionsAsync(Guid centerId, Guid teacherId, DateTimeOffset now, CancellationToken token) =>
        db.Sessions.AnyAsync(x => x.CenterId == centerId && x.TeacherId == teacherId &&
            x.CancelledAtUtc == null && x.NotHeldAtUtc == null && x.EndUtc > now, token);
    public Task<bool> HasActiveMakeupAsync(Guid centerId, Guid originalSessionId, CancellationToken token) =>
        db.Sessions.AnyAsync(x => x.CenterId == centerId && x.MakeupForSessionId == originalSessionId &&
            x.CancelledAtUtc == null && x.NotHeldAtUtc == null, token);
    public Task<bool> SessionHasAttendanceAsync(Guid centerId, Guid sessionId, CancellationToken token) =>
        db.AttendanceSheets.AnyAsync(x => x.CenterId == centerId && x.Id == sessionId, token);
    public Task<bool> AnySessionHasAttendanceAsync(Guid centerId, IReadOnlyCollection<Guid> sessionIds, CancellationToken token) =>
        db.AttendanceSheets.AnyAsync(x => x.CenterId == centerId && sessionIds.Contains(x.Id), token);

    public async Task AddAsync<T>(T entity, CancellationToken token) where T : class =>
        await db.Set<T>().AddAsync(entity, token);
    public Task AddSessionsAsync(IReadOnlyList<TeachingSession> sessions, CancellationToken token) =>
        db.Sessions.AddRangeAsync(sessions, token);

    public async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new TeachingPersistenceException("", true, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation or
                PostgresErrorCodes.ExclusionViolation)
        { throw new TeachingPersistenceException(pg.ConstraintName ?? "", false, ex,
            pg.SqlState == PostgresErrorCodes.ExclusionViolation); }
    }

    public Task<bool> TryAddSessionAsync(TeachingSession session, CancellationToken token) =>
        TryAddSessionAsync(session, () => Task.CompletedTask, token);

    public async Task<bool> TryAddSessionAsync(TeachingSession session, Func<Task> beforeCommit, CancellationToken token)
    {
        await using var transaction = await BeginGuardAsync(session.CenterId, session.ClassId, null,
            session.TeacherId, session.RoomId, token);

        if (!await db.Classes.AnyAsync(x => x.CenterId == session.CenterId && x.Id == session.ClassId &&
            x.ArchivedAtUtc == null, token) ||
            !await db.Teachers.AnyAsync(x => x.CenterId == session.CenterId && x.Id == session.TeacherId &&
            x.ArchivedAtUtc == null, token) ||
            !await RoomIsActiveAsync(session.CenterId, session.RoomId, token)) return false;

        var overlaps = await db.Sessions.AnyAsync(x => x.CenterId == session.CenterId &&
            x.CancelledAtUtc == null && x.NotHeldAtUtc == null && x.StartUtc < session.EndUtc && x.EndUtc > session.StartUtc &&
            (x.ClassId == session.ClassId || x.TeacherId == session.TeacherId || x.RoomId == session.RoomId), token);
        if (overlaps) return false;

        await db.Sessions.AddAsync(session, token);
        try
        {
            await SaveAsync(token);
            await beforeCommit();
            await SaveAsync(token);
        }
        catch (TeachingPersistenceException ex) when (ex.IsScheduleConflict) { return false; }
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<ITeachingGuard> BeginGuardAsync(Guid centerId, Guid? classId, Guid? studentId,
        Guid? teacherId, Guid? roomId, CancellationToken token)
    {
        var keys = new List<string>();
        if (classId.HasValue) keys.Add($"class:{centerId}:{classId.Value}");
        if (studentId.HasValue) keys.Add($"student:{centerId}:{studentId.Value}");
        if (teacherId.HasValue) keys.Add($"teacher:{centerId}:{teacherId.Value}");
        if (roomId.HasValue) keys.Add($"room:{centerId}:{roomId.Value}");
        return await BeginLocksAsync(keys, token);
    }

    public Task<ITeachingGuard> BeginEnrollmentGuardAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        Guid studentId, CancellationToken token) =>
        BeginLocksAsync(classIds.Select(id => $"class:{centerId}:{id}").Append($"student:{centerId}:{studentId}"), token);

    public async Task<IReadOnlyDictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)>> ReadPreviousTimesAsync(
        Guid centerId, IReadOnlyCollection<Guid> sessionIds, CancellationToken token)
    {
        var rows = await db.ScheduleChanges.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.SessionId != null && sessionIds.Contains(x.SessionId.Value) &&
                x.Kind == ScheduleChangeKind.SessionRescheduled && x.OldStartUtc != null && x.OldEndUtc != null)
            .Select(x => new { SessionId = x.SessionId!.Value, x.OldStartUtc, x.OldEndUtc, x.CreatedAtUtc })
            .ToListAsync(token);
        return rows.GroupBy(x => x.SessionId).ToDictionary(x => x.Key, x =>
        {
            var latest = x.OrderByDescending(r => r.CreatedAtUtc).First();
            return (latest.OldStartUtc!.Value, latest.OldEndUtc!.Value);
        });
    }

    public Task<ITeachingGuard> BeginScheduleGuardAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, IReadOnlyCollection<Guid> teacherIds,
        IReadOnlyCollection<Guid> roomIds, CancellationToken token) =>
        BeginLocksAsync(classIds.Select(id => $"class:{centerId}:{id}")
            .Concat(teacherIds.Select(id => $"teacher:{centerId}:{id}"))
            .Concat(roomIds.Select(id => $"room:{centerId}:{id}")), token);

    private async Task<ITeachingGuard> BeginLocksAsync(IEnumerable<string> keys, CancellationToken token)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            foreach (var key in keys.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new TeachingGuard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class TeachingGuard(IDbContextTransaction transaction) : ITeachingGuard
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
