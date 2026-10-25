namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record AttendanceStudentDto(Guid StudentId, string StudentCode,
    string FullName, string Mark, string? Note)
{
    /// <summary>B8: the center approved this student's absence request for the session.</summary>
    public bool ExcusedAbsenceApproved { get; init; }
}
public sealed record AttendanceSheetDto(Guid SessionId, Guid ClassId, DateTimeOffset StartUtc,
    string Status, int Version, Guid? ActualTeacherId, string? Note,
    DateTimeOffset? SubmittedAtUtc, IReadOnlyList<AttendanceStudentDto> Students)
{
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? ActualTeacherName { get; init; }
}
public sealed record AttendanceSnapshotStudentDto(Guid StudentId, string Mark, string? Note);
public sealed record AttendanceSnapshotDto(string Status, Guid? ActualTeacherId, string? Note,
    IReadOnlyList<AttendanceSnapshotStudentDto> Students);
public sealed record AttendanceRevisionDto(int Version, string Action, Guid ActorUserId,
    string? Reason, DateTimeOffset CreatedAtUtc, AttendanceSnapshotDto Before,
    AttendanceSnapshotDto After)
{
    public string? ActorName { get; init; }
}
