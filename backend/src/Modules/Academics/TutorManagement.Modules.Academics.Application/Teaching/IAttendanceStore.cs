using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record AttendanceRosterRow(Guid EnrollmentId, Guid StudentId, string StudentCode, string FullName);

public interface IAttendanceStore
{
    Task<SessionAttendance?> FindSheetAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task<IReadOnlyList<AttendanceEntry>> ListEntriesAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task<IReadOnlyList<AttendanceRosterRow>> ListRosterAsync(Guid centerId, Guid classId,
        DateTimeOffset sessionStartUtc, CancellationToken token);
    Task<IReadOnlyList<AttendanceRevision>> ListRevisionsAsync(Guid centerId, Guid sessionId, CancellationToken token);
    /// <summary>B8: approved absence requests for the session, by student, with the student's reason.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListApprovedAbsencesAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task AddSheetAsync(SessionAttendance sheet, CancellationToken token);
    Task AddEntriesAsync(IEnumerable<AttendanceEntry> entries, CancellationToken token);
    Task AddRevisionAsync(AttendanceRevision revision, CancellationToken token);
}
