using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public enum ClassApplicationStatus { Pending, Approved, Rejected }

public sealed class ClassApplication : Entity<Guid>
{
    private ClassApplication() : base(Guid.Empty) { }

    private ClassApplication(Guid centerId, Guid classId, Guid userId, Guid submissionId,
        string idempotencyKey, string requestHash, string fullName, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        UserId = userId;
        SubmissionId = submissionId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        FullName = TeachingClass.NormalizeName(fullName);
        SubmittedAtUtc = now;
        Status = ClassApplicationStatus.Pending;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? ParentName { get; private set; }
    public string? ParentPhone { get; private set; }
    public ClassApplicationStatus Status { get; private set; }
    public DateTimeOffset SubmittedAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewReason { get; private set; }

    public static ClassApplication Create(Guid centerId, Guid classId, Guid userId, Guid submissionId,
        string idempotencyKey, string requestHash, string fullName, DateTimeOffset now,
        string? parentName = null, string? parentPhone = null)
    {
        string? phone = null;
        if ((parentName is not null || parentPhone is not null) &&
            (!ParentContact.IsValidName(parentName) || !ParentContact.TryNormalizePhone(parentPhone, out phone)))
            throw new ArgumentException("Parent contact is invalid.");
        if (centerId == Guid.Empty || classId == Guid.Empty || userId == Guid.Empty || submissionId == Guid.Empty ||
            !TeachingClass.IsValidName(fullName) || string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 80 || requestHash.Length != 64)
            throw new ArgumentException("Class application details are invalid.");
        var application = new ClassApplication(centerId, classId, userId, submissionId, idempotencyKey, requestHash,
            fullName, now);
        if (phone is not null)
        {
            application.ParentName = TeachingClass.NormalizeName(parentName!);
            application.ParentPhone = phone;
        }
        return application;
    }

    public void Approve(Guid reviewerId, DateTimeOffset now)
    {
        if (Status != ClassApplicationStatus.Pending || reviewerId == Guid.Empty)
            throw new InvalidOperationException("Only a pending application can be approved.");
        Status = ClassApplicationStatus.Approved;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = now;
    }

    public void Reject(Guid reviewerId, string reason, DateTimeOffset now)
    {
        if (Status != ClassApplicationStatus.Pending || reviewerId == Guid.Empty ||
            string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            throw new InvalidOperationException("A pending application requires a rejection reason.");
        Status = ClassApplicationStatus.Rejected;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = now;
        ReviewReason = reason.Trim();
    }
}
