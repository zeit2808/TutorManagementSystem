using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

public enum DiscountKind
{
    Percent = 1,
    Amount = 2
}

/// <summary>
/// H8: a reduction for one student in one class, for the cycles that start between <see cref="FromDate"/> and
/// <see cref="ToDate"/>. A reason is always kept. Revoking stops it for cycles billed afterwards.
/// </summary>
public sealed class TuitionDiscount : Entity<Guid>
{
    public const int MaxReasonLength = 300;

    private TuitionDiscount() : base(Guid.Empty) { }

    private TuitionDiscount(Guid centerId, Guid studentId, Guid classId, DiscountKind kind, long value,
        DateOnly fromDate, DateOnly? toDate, string reason, Guid createdByUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        StudentId = studentId;
        ClassId = classId;
        Kind = kind;
        Value = value;
        FromDate = fromDate;
        ToDate = toDate;
        Reason = reason;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid ClassId { get; private set; }
    public DiscountKind Kind { get; private set; }
    /// <summary>Percent (1–100) or an amount in VND (multiple of 1,000).</summary>
    public long Value { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly? ToDate { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }

    public static bool IsValid(DiscountKind kind, long value, DateOnly fromDate, DateOnly? toDate, string? reason) =>
        kind switch
        {
            DiscountKind.Percent => value is >= 1 and <= 100,
            DiscountKind.Amount => value is > 0 and <= TuitionMath.MaxFee && value % 1000 == 0,
            _ => false
        } && (toDate is null || toDate >= fromDate) && !string.IsNullOrWhiteSpace(reason) &&
        reason.Trim().Length is >= 3 and <= MaxReasonLength;

    public static TuitionDiscount Create(Guid centerId, Guid studentId, Guid classId, DiscountKind kind, long value,
        DateOnly fromDate, DateOnly? toDate, string reason, Guid createdByUserId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || studentId == Guid.Empty || classId == Guid.Empty || createdByUserId == Guid.Empty ||
            !IsValid(kind, value, fromDate, toDate, reason)) throw new ArgumentException("Discount details are invalid.");
        return new(centerId, studentId, classId, kind, value, fromDate, toDate, reason.Trim(), createdByUserId, now);
    }

    public bool AppliesTo(DateOnly cycleStart) =>
        RevokedAtUtc is null && FromDate <= cycleStart && (ToDate is null || ToDate >= cycleStart);

    public void Revoke(Guid actorId, DateTimeOffset now)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = now;
        RevokedByUserId = actorId;
    }

    /// <summary>The profile was merged into another one (M3); the discount follows.</summary>
    public void MoveToStudent(Guid studentId)
    {
        if (studentId != Guid.Empty) StudentId = studentId;
    }

    /// <summary>Total reduction of <paramref name="listAmount"/>: percents add up (capped at 100%), then amounts.</summary>
    public static long Total(IEnumerable<TuitionDiscount> discounts, long listAmount)
    {
        long percent = 0, fixedAmount = 0;
        foreach (var discount in discounts)
        {
            if (discount.Kind == DiscountKind.Percent) percent += discount.Value;
            else fixedAmount += discount.Value;
        }
        var total = TuitionMath.RoundToThousand(listAmount * (decimal)Math.Min(percent, 100) / 100) + fixedAmount;
        return Math.Clamp(total, 0, listAmount);
    }
}
