namespace TutorManagement.Modules.Academics.Contracts.Teaching;

public sealed record CreateClassRequest(Guid SubjectId, string Code, string Name, int MaxStudents);
public sealed record UpdateClassRequest(string Name, int MaxStudents, int? ExpectedVersion);
/// <summary>Leave Code empty to get the next HS-000001 code. Parent name and mobile are required.</summary>
public sealed record CreateStudentRequest(string? Code, string FullName, string ParentName, string ParentPhone);
public sealed record UpdateStudentRequest(string FullName, string ParentName, string ParentPhone, int? ExpectedVersion);
public sealed record CreateTeacherRequest(string Code, string FullName);
public sealed record UpdateTeacherRequest(string FullName, int? ExpectedVersion);
/// <summary>B5: set AcknowledgeConflicts after the center has seen the student's clashing classes.</summary>
public sealed record EnrollStudentRequest(Guid StudentId, bool? AcknowledgeConflicts = null);
/// <summary>B5: what adding or moving a session means for the class's students.</summary>
/// <summary>TeacherId (optional): also count that teacher's sessions at other centers at that time.</summary>
public sealed record SessionImpactRequest(Guid ClassId, DateTimeOffset StartUtc, DateTimeOffset EndUtc, Guid? SessionId = null,
    Guid? TeacherId = null);
public sealed record CheckClassApplicationsRequest(IReadOnlyList<Guid> ClassIds);
public sealed record ApproveClassApplicationRequest(bool? AcknowledgeConflicts = null);
/// <summary>B8: Kind is ExcusedAbsence, Makeup, Transfer or Withdrawal.</summary>
public sealed record SubmitStudentRequestRequest(string Kind, Guid ClassId, Guid? SessionId, Guid? TargetClassId,
    Guid? NoticeId, string Reason);
public sealed record ApproveStudentRequestRequest(int? ExpectedVersion, string? Note = null, bool? AcknowledgeConflicts = null);
public sealed record RejectStudentRequestRequest(int? ExpectedVersion, string Reason);
public sealed record WithdrawStudentRequest(string Reason);
/// <summary>Set MakeupForSessionId to make up for a cancelled or not-held session of the same class.</summary>
public sealed record CreateSessionRequest(Guid ClassId, Guid TeacherId, Guid RoomId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, Guid? MakeupForSessionId = null);
/// <summary>The reason is required and shown to students and staff (D7).</summary>
public sealed record RescheduleSessionRequest(Guid TeacherId, Guid RoomId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, int? ExpectedVersion, string? Reason = null);
/// <summary>Cancel a future session, or record that a started session did not take place (D6/D7).</summary>
public sealed record SessionStatusChangeRequest(string? Reason, int? ExpectedVersion);
/// <summary>TimeZoneId may be omitted; only Asia/Ho_Chi_Minh is accepted (D12).</summary>
public sealed record CreateScheduleSeriesRequest(Guid RequestId, Guid ClassId, Guid TeacherId, Guid RoomId,
    string? TimeZoneId, DateOnly StartDate, DateOnly EndDate, TimeOnly LocalStartTime,
    int DurationMinutes, IReadOnlyList<string> Weekdays, IReadOnlyList<DateOnly>? ExcludedDates);
/// <summary>B6: the reason is required and shown to students; under 7 days' notice needs AcknowledgeShortNotice.</summary>
public sealed record StopScheduleSeriesRequest(DateOnly FromDate, int? ExpectedVersion, string? Reason = null,
    bool? AcknowledgeShortNotice = null);
public sealed record ReplaceScheduleSeriesRequest(DateOnly FromDate, int? ExpectedVersion,
    CreateScheduleSeriesRequest Replacement, string? Reason = null, bool? AcknowledgeShortNotice = null);
public sealed record TeachingVersionRequest(int? ExpectedVersion);
public sealed record LinkTeacherAccountRequest(Guid? UserId, int? ExpectedVersion);
