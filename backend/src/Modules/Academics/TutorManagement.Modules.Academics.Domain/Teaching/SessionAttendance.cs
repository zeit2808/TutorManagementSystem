using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public enum AttendanceSheetStatus { Draft, Submitted }
public enum AttendanceMark { Unknown, Present, ExcusedAbsent, UnexcusedAbsent, Late }

public sealed class SessionAttendance : AggregateRoot<Guid>
{
    private SessionAttendance() : base(Guid.Empty) { }

    private SessionAttendance(Guid centerId, Guid sessionId, Guid classId,
        DateTimeOffset now, Guid actorId) : base(sessionId)
    {
        CenterId = centerId;
        ClassId = classId;
        Status = AttendanceSheetStatus.Draft;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public AttendanceSheetStatus Status { get; private set; }
    public int Version { get; private set; }
    public Guid? ActualTeacherId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public Guid? SubmittedByUserId { get; private set; }

    public static SessionAttendance Create(Guid centerId, Guid sessionId,
        Guid classId, DateTimeOffset now, Guid actorId)
    {
        if (centerId == Guid.Empty || sessionId == Guid.Empty || classId == Guid.Empty || actorId == Guid.Empty)
            throw new ArgumentException("Attendance identity is invalid.");
        return new(centerId, sessionId, classId, now, actorId);
    }

    public void SaveDraft(Guid? actualTeacherId, string? note, DateTimeOffset now, Guid actorId)
    {
        if (Status != AttendanceSheetStatus.Draft)
            throw new InvalidOperationException("Submitted attendance must use a correction.");
        ActualTeacherId = actualTeacherId;
        Note = NormalizeNote(note);
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
        Version++;
    }

    public void Submit(DateTimeOffset now, Guid actorId)
    {
        if (Status != AttendanceSheetStatus.Draft || ActualTeacherId is null)
            throw new InvalidOperationException("A complete draft with an actual teacher is required.");
        Status = AttendanceSheetStatus.Submitted;
        SubmittedAtUtc = now;
        SubmittedByUserId = actorId;
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
        Version++;
    }

    public void Correct(Guid? actualTeacherId, string? note, DateTimeOffset now, Guid actorId)
    {
        if (Status != AttendanceSheetStatus.Submitted || actualTeacherId is null)
            throw new InvalidOperationException("Only submitted attendance can be corrected.");
        ActualTeacherId = actualTeacherId;
        Note = NormalizeNote(note);
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
        Version++;
    }

    public static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}

public sealed class AttendanceEntry : Entity<Guid>
{
    private AttendanceEntry() : base(Guid.Empty) { }
    private AttendanceEntry(Guid centerId, Guid sessionId, Guid enrollmentId,
        Guid studentId, AttendanceMark mark, string? note, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        SessionId = sessionId;
        EnrollmentId = enrollmentId;
        StudentId = studentId;
        Mark = mark;
        Note = SessionAttendance.NormalizeNote(note);
        UpdatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    public Guid StudentId { get; private set; }
    public AttendanceMark Mark { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static AttendanceEntry Create(Guid centerId, Guid sessionId, Guid enrollmentId,
        Guid studentId, AttendanceMark mark, string? note, DateTimeOffset now) =>
        new(centerId, sessionId, enrollmentId, studentId, mark, note, now);

    public void Change(AttendanceMark mark, string? note, DateTimeOffset now)
    {
        Mark = mark;
        Note = SessionAttendance.NormalizeNote(note);
        UpdatedAtUtc = now;
    }
}

public sealed class AttendanceRevision : Entity<Guid>
{
    private AttendanceRevision() : base(Guid.Empty) { }
    private AttendanceRevision(Guid centerId, Guid sessionId, int version, string action,
        Guid actorUserId, string? reason, string beforeJson, string afterJson,
        DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        SessionId = sessionId;
        Version = version;
        Action = action;
        ActorUserId = actorUserId;
        Reason = reason;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid SessionId { get; private set; }
    public int Version { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public string BeforeJson { get; private set; } = string.Empty;
    public string AfterJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AttendanceRevision Create(Guid centerId, Guid sessionId, int version,
        string action, Guid actorUserId, string? reason, string beforeJson,
        string afterJson, DateTimeOffset now) =>
        new(centerId, sessionId, version, action, actorUserId, reason, beforeJson, afterJson, now);
}
