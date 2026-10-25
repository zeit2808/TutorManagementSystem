namespace TutorManagement.Modules.Academics.Contracts.Teaching;

public sealed record AttendanceOverrideRequest(Guid StudentId, string Mark, string? Note);
public sealed record SaveAttendanceDraftRequest(int ExpectedVersion, Guid? ActualTeacherId,
    string? Note, string DefaultMark, IReadOnlyList<AttendanceOverrideRequest> Overrides);
public sealed record SubmitAttendanceRequest(int ExpectedVersion);
public sealed record CorrectAttendanceRequest(int ExpectedVersion, Guid ActualTeacherId,
    string? Note, string Reason, IReadOnlyList<AttendanceOverrideRequest> Changes);
