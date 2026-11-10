using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payments;

/// <summary>Who changed payment settings or decided about a transaction, and how. Insert only; never holds a secret.</summary>
public sealed class PaymentAuditEvent : Entity<Guid>
{
    public const int MaxNameLength = 64;

    private PaymentAuditEvent() : base(Guid.Empty) { }

    private PaymentAuditEvent(Guid centerId, Guid? actorUserId, string action, string targetType, Guid? targetId,
        string? beforeJson, string? afterJson, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        AtUtc = now;
    }

    public Guid CenterId { get; private set; }

    /// <summary>Null when the system acted (for example the Worker holding a duplicate).</summary>
    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid? TargetId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }

    public static PaymentAuditEvent Record(Guid centerId, Guid? actorUserId, string action, string targetType, Guid? targetId,
        string? beforeJson, string? afterJson, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || actorUserId == Guid.Empty || Invalid(action) || Invalid(targetType))
            throw new ArgumentException("Audit event details are invalid.");
        return new(centerId, actorUserId, action, targetType, targetId, beforeJson, afterJson, now);
    }

    private static bool Invalid(string value) => string.IsNullOrWhiteSpace(value) || value.Length > MaxNameLength;
}
