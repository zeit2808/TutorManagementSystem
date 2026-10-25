using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public enum StudentRequestKind
{
    /// <summary>Xin vắng có phép một buổi; sent before the session starts.</summary>
    ExcusedAbsence = 0,
    /// <summary>Xin học bù / học ghép cho một buổi.</summary>
    Makeup = 1,
    /// <summary>Xin chuyển sang lớp khác của cùng trung tâm.</summary>
    Transfer = 2,
    /// <summary>Xin rút khỏi lớp.</summary>
    Withdrawal = 3
}

public enum StudentRequestStatus { Pending = 0, Approved = 1, Rejected = 2, Cancelled = 3 }

/// <summary>
/// A request a student sends to their center about one class (and, for absence or makeup, one session).
/// The center owner or manager approves or rejects it; approving a transfer or withdrawal changes enrollment.
/// </summary>
public sealed class StudentRequest : Entity<Guid>
{
    public const int MaxReasonLength = 500;

    private StudentRequest() : base(Guid.Empty) { }

    private StudentRequest(Guid centerId, Guid studentId, Guid userId, Guid classId, StudentRequestKind kind,
        Guid? sessionId, Guid? targetClassId, Guid? noticeId, string reason, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        StudentId = studentId;
        UserId = userId;
        ClassId = classId;
        Kind = kind;
        SessionId = sessionId;
        TargetClassId = targetClassId;
        NoticeId = noticeId;
        Reason = reason.Trim();
        Status = StudentRequestStatus.Pending;
        SubmittedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ClassId { get; private set; }
    public StudentRequestKind Kind { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? TargetClassId { get; private set; }
    public Guid? NoticeId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public StudentRequestStatus Status { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewNote { get; private set; }
    public int Version { get; private set; }

    public bool NeedsSession => Kind is StudentRequestKind.ExcusedAbsence or StudentRequestKind.Makeup;

    public static StudentRequest Create(Guid centerId, Guid studentId, Guid userId, Guid classId,
        StudentRequestKind kind, Guid? sessionId, Guid? targetClassId, Guid? noticeId, string reason, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || studentId == Guid.Empty || userId == Guid.Empty || classId == Guid.Empty ||
            !Enum.IsDefined(kind) || !IsValidReason(reason))
            throw new ArgumentException("Request details are invalid.");
        var needsSession = kind is StudentRequestKind.ExcusedAbsence or StudentRequestKind.Makeup;
        if (needsSession != (sessionId is { } id && id != Guid.Empty))
            throw new ArgumentException("Absence and makeup requests name exactly one session.", nameof(sessionId));
        if ((kind == StudentRequestKind.Transfer) != (targetClassId is { } target && target != Guid.Empty && target != classId))
            throw new ArgumentException("A transfer names a different target class.", nameof(targetClassId));
        return new(centerId, studentId, userId, classId, kind, needsSession ? sessionId : null,
            kind == StudentRequestKind.Transfer ? targetClassId : null, noticeId == Guid.Empty ? null : noticeId,
            reason, now);
    }

    public void Approve(Guid reviewerId, string? note, DateTimeOffset now) =>
        Review(StudentRequestStatus.Approved, reviewerId, note, now);

    public void Reject(Guid reviewerId, string reason, DateTimeOffset now)
    {
        if (!IsValidReason(reason)) throw new ArgumentException("A rejection reason is required.", nameof(reason));
        Review(StudentRequestStatus.Rejected, reviewerId, reason, now);
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status != StudentRequestStatus.Pending) throw new InvalidOperationException("Only a pending request can be cancelled.");
        Status = StudentRequestStatus.Cancelled;
        ReviewedAtUtc = now;
        Version++;
    }

    private void Review(StudentRequestStatus status, Guid reviewerId, string? note, DateTimeOffset now)
    {
        if (Status != StudentRequestStatus.Pending || reviewerId == Guid.Empty)
            throw new InvalidOperationException("Only a pending request can be reviewed.");
        if (note is not null && note.Trim().Length > MaxReasonLength) throw new ArgumentException("The note is too long.", nameof(note));
        Status = status;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = now;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Version++;
    }

    public static bool IsValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= MaxReasonLength &&
        !reason.Any(c => char.IsControl(c) && c != '\n');
}
