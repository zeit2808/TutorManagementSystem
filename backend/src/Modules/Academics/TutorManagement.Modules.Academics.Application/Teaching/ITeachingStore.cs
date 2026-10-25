using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>Optional narrowing of a session list; null fields do not filter.</summary>
public sealed record SessionFilter(Guid? ClassId = null, Guid? TeacherId = null, Guid? RoomId = null)
{
    public static readonly SessionFilter None = new();
}

public interface ITeachingStore
{
    Task<bool> SubjectIsActiveAsync(Guid centerId, Guid subjectId, CancellationToken token);
    Task<bool> RoomIsActiveAsync(Guid centerId, Guid roomId, CancellationToken token);
    Task<TeachingClass?> FindClassAsync(Guid centerId, Guid id, CancellationToken token);
    Task<(IReadOnlyList<TeachingClass> Items, int TotalCount)> ListClassesAsync(Guid centerId, bool includeArchived, string? search, int page, int pageSize, CancellationToken token);
    Task<bool> ClassCodeExistsAsync(Guid centerId, string code, CancellationToken token);
    Task<Student?> FindStudentAsync(Guid centerId, Guid id, CancellationToken token);
    Task<(IReadOnlyList<Student> Items, int TotalCount)> ListStudentsAsync(Guid centerId, bool includeArchived, string? search, int page, int pageSize, CancellationToken token);
    Task<string> NextStudentCodeAsync(Guid centerId, CancellationToken token);
    Task<bool> StudentCodeExistsAsync(Guid centerId, string code, CancellationToken token);
    Task<Teacher?> FindTeacherAsync(Guid centerId, Guid id, CancellationToken token);
    Task<(IReadOnlyList<Teacher> Items, int TotalCount)> ListTeachersAsync(Guid centerId, bool includeArchived, string? search, int page, int pageSize, CancellationToken token);
    Task<bool> TeacherCodeExistsAsync(Guid centerId, string code, CancellationToken token);
    Task<Teacher?> FindTeacherByUserAsync(Guid centerId, Guid userId, CancellationToken token);
    Task<IReadOnlyList<TeachingSession>> ListTeacherSessionsAsync(Guid centerId, Guid teacherId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token);
    Task<Enrollment?> FindActiveEnrollmentAsync(Guid centerId, Guid classId, Guid studentId, CancellationToken token);
    Task<(IReadOnlyList<ClassStudentDto> Items, int TotalCount)> ListClassStudentsAsync(
        Guid centerId, Guid classId, EnrollmentFilter status, int page, int pageSize, CancellationToken token);
    Task<bool> StudentHasActiveEnrollmentAsync(Guid centerId, Guid studentId, CancellationToken token);
    Task<TeachingSession?> FindSessionAsync(Guid centerId, Guid id, CancellationToken token);
    Task<TeachingSession?> FindSessionSnapshotAsync(Guid centerId, Guid id, CancellationToken token);
    Task<ScheduleSeries?> FindSeriesAsync(Guid centerId, Guid id, CancellationToken token);
    Task<ScheduleSeries?> FindSeriesForUpdateAsync(Guid centerId, Guid id, CancellationToken token);
    Task<ScheduleSeries?> FindSeriesByRequestIdAsync(Guid centerId, Guid requestId, CancellationToken token);
    Task<(IReadOnlyList<ScheduleSeries> Items, int TotalCount)> ListSeriesAsync(
        Guid centerId, Guid? classId, int page, int pageSize, CancellationToken token);
    Task<IReadOnlyList<TeachingSession>> ListSeriesSessionsAsync(Guid centerId, Guid seriesId,
        DateOnly fromDate, CancellationToken token);
    Task<IReadOnlyList<TeachingSession>> ListResourceSessionsAsync(Guid centerId,
        Guid classId, Guid teacherId, Guid roomId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken token);
    Task<IReadOnlyList<TeachingSession>> ListSessionsAsync(Guid centerId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, SessionFilter filter, CancellationToken token);
    Task<bool> ClassHasFutureSessionsAsync(Guid centerId, Guid classId, DateTimeOffset now, CancellationToken token);
    Task<bool> TeacherHasFutureSessionsAsync(Guid centerId, Guid teacherId, DateTimeOffset now, CancellationToken token);
    Task<bool> HasActiveMakeupAsync(Guid centerId, Guid originalSessionId, CancellationToken token);
    Task<bool> SessionHasAttendanceAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task<bool> AnySessionHasAttendanceAsync(Guid centerId, IReadOnlyCollection<Guid> sessionIds, CancellationToken token);
    Task AddAsync<T>(T entity, CancellationToken token) where T : class;
    Task AddSessionsAsync(IReadOnlyList<TeachingSession> sessions, CancellationToken token);
    Task SaveAsync(CancellationToken token);
    Task<bool> TryAddSessionAsync(TeachingSession session, CancellationToken token);
    Task<ITeachingGuard> BeginGuardAsync(Guid centerId, Guid? classId, Guid? studentId,
        Guid? teacherId, Guid? roomId, CancellationToken token);
    /// <summary>Locks the classes and the student (same keys as <see cref="BeginGuardAsync"/>).</summary>
    Task<ITeachingGuard> BeginEnrollmentGuardAsync(Guid centerId, IReadOnlyCollection<Guid> classIds, Guid studentId,
        CancellationToken token);
    /// <summary>Like TryAddSessionAsync but runs <paramref name="beforeCommit"/> inside the same transaction.</summary>
    Task<bool> TryAddSessionAsync(TeachingSession session, Func<Task> beforeCommit, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)>> ReadPreviousTimesAsync(Guid centerId,
        IReadOnlyCollection<Guid> sessionIds, CancellationToken token);
    Task<ITeachingGuard> BeginScheduleGuardAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, IReadOnlyCollection<Guid> teacherIds,
        IReadOnlyCollection<Guid> roomIds, CancellationToken token);
}

public interface ITeachingGuard : IAsyncDisposable
{
    Task CommitAsync(CancellationToken token);
}
