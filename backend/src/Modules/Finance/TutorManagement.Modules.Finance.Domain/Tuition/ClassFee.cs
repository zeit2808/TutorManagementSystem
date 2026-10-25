using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>
/// H1–H3: the monthly fee of a class from a given day. A cycle uses the fee whose <see cref="EffectiveFrom"/> is on or
/// before the cycle's first day, so a change made mid-cycle applies from the next cycle. Rows are never edited.
/// </summary>
public sealed class ClassFee : Entity<Guid>
{
    private ClassFee() : base(Guid.Empty) { }

    private ClassFee(Guid centerId, Guid classId, long monthlyFee, DateOnly effectiveFrom, Guid createdByUserId,
        DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        MonthlyFee = monthlyFee;
        EffectiveFrom = effectiveFrom;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public long MonthlyFee { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ClassFee Create(Guid centerId, Guid classId, long monthlyFee, DateOnly effectiveFrom,
        Guid createdByUserId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty || createdByUserId == Guid.Empty)
            throw new ArgumentException("Fee owner is required.");
        if (!TuitionMath.IsValidFee(monthlyFee)) throw new ArgumentOutOfRangeException(nameof(monthlyFee));
        return new(centerId, classId, monthlyFee, effectiveFrom, createdByUserId, now);
    }

    /// <summary>
    /// The fee for a cycle starting on <paramref name="cycleStart"/>: the latest one effective by then. When no fee
    /// was effective yet, the very first fee still covers the cycle it was set in, so the running month is billed.
    /// </summary>
    public static ClassFee? ForCycle(IReadOnlyList<ClassFee> history, DateOnly cycleStart, DateOnly cycleEnd)
    {
        ClassFee? best = null;
        foreach (var fee in history)
            if (fee.EffectiveFrom <= cycleStart && (best is null || IsLater(fee, best))) best = fee;
        if (best is not null) return best;
        ClassFee? first = null;
        foreach (var fee in history)
            if (first is null || IsEarlier(fee, first)) first = fee;
        return first is not null && first.EffectiveFrom <= cycleEnd ? first : null;
    }

    private static bool IsLater(ClassFee a, ClassFee b) =>
        a.EffectiveFrom > b.EffectiveFrom || (a.EffectiveFrom == b.EffectiveFrom && a.CreatedAtUtc > b.CreatedAtUtc);

    private static bool IsEarlier(ClassFee a, ClassFee b) =>
        a.EffectiveFrom < b.EffectiveFrom || (a.EffectiveFrom == b.EffectiveFrom && a.CreatedAtUtc < b.CreatedAtUtc);
}
