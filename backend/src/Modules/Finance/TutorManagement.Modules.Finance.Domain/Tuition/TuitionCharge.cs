using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>
/// One cycle of one enrollment (H9): created when the cycle starts, for a fixed amount. Joining mid-cycle bills a
/// share of the sessions (H7); leaving mid-cycle lowers the amount to what was already paid and keeps a note of the
/// sessions attended. The paid part lives in receipt allocations.
/// </summary>
public sealed class TuitionCharge : Entity<Guid>
{
    private TuitionCharge() : base(Guid.Empty) { }

    private TuitionCharge(Guid centerId, Guid classId, Guid studentId, Guid enrollmentId, int cycleIndex,
        DateOnly periodStart, DateOnly periodEnd, long monthlyFee, int? joinSessions, int? periodSessions,
        long listAmount, long discountAmount, DateOnly dueDate, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        StudentId = studentId;
        EnrollmentId = enrollmentId;
        CycleIndex = cycleIndex;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        MonthlyFee = monthlyFee;
        JoinSessions = joinSessions;
        PeriodSessions = periodSessions;
        ListAmount = listAmount;
        DiscountAmount = discountAmount;
        Amount = listAmount - discountAmount;
        DueDate = dueDate;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    public Guid EnrollmentId { get; private set; }
    /// <summary>0 = the class's first cycle.</summary>
    public int CycleIndex { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    /// <summary>Last day, inclusive.</summary>
    public DateOnly PeriodEnd { get; private set; }
    /// <summary>The class fee this cycle was billed at.</summary>
    public long MonthlyFee { get; private set; }
    /// <summary>Joined mid-cycle: sessions from the student's first one to the end of the cycle.</summary>
    public int? JoinSessions { get; private set; }
    /// <summary>Joined mid-cycle: sessions in the whole cycle.</summary>
    public int? PeriodSessions { get; private set; }
    /// <summary>Fee, or its share when joined mid-cycle, before discounts.</summary>
    public long ListAmount { get; private set; }
    public long DiscountAmount { get; private set; }
    /// <summary>What the student owes for this cycle.</summary>
    public long Amount { get; private set; }
    public DateOnly DueDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    /// <summary>Left the class during this cycle (local date of the withdrawal).</summary>
    public DateOnly? EndedOn { get; private set; }
    public int? EndedSessionsAttended { get; private set; }
    public int? EndedSessionsTotal { get; private set; }
    /// <summary>What the attended sessions would have cost — a note only, never billed.</summary>
    public long? EndedValue { get; private set; }
    /// <summary>The unpaid part dropped when the student left.</summary>
    public long WaivedAmount { get; private set; }
    public DateTimeOffset? RemindedAtUtc { get; private set; }
    /// <summary>H18: reduction the center chose for missed sessions.</summary>
    public long AdjustmentAmount { get; private set; }
    public int? AdjustmentSessions { get; private set; }
    public string? AdjustmentReason { get; private set; }
    public DateTimeOffset? AdjustedAtUtc { get; private set; }
    public Guid? AdjustedByUserId { get; private set; }
    public int Version { get; private set; }

    public const int MaxReasonLength = 300;

    public bool JoinedMidCycle => JoinSessions.HasValue;

    public static TuitionCharge Create(Guid centerId, Guid classId, Guid studentId, Guid enrollmentId, int cycleIndex,
        DateOnly periodStart, DateOnly periodEnd, long monthlyFee, int? joinSessions, int? periodSessions,
        IEnumerable<TuitionDiscount> discounts, DateOnly dueDate, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty || studentId == Guid.Empty || enrollmentId == Guid.Empty ||
            cycleIndex < 0 || periodEnd < periodStart || !TuitionMath.IsValidFee(monthlyFee) || dueDate < periodStart)
            throw new ArgumentException("Charge details are invalid.");
        if (joinSessions.HasValue != periodSessions.HasValue || joinSessions < 0 || joinSessions > periodSessions)
            throw new ArgumentException("Join share is invalid.");
        var list = joinSessions is { } part && periodSessions is { } whole
            ? TuitionMath.Prorate(monthlyFee, part, whole) : monthlyFee;
        var discount = TuitionDiscount.Total(discounts.Where(x => x.AppliesTo(periodStart)), list);
        return new(centerId, classId, studentId, enrollmentId, cycleIndex, periodStart, periodEnd, monthlyFee,
            joinSessions, periodSessions, list, discount, dueDate, now);
    }

    /// <summary>H7, leaving mid-cycle: keep what was paid, drop the rest, note the sessions attended.</summary>
    public void EndEarly(DateOnly endedOn, int attended, int total, long paid)
    {
        if (EndedOn is not null) return;
        if (attended < 0 || total < 0 || attended > total || paid < 0) throw new ArgumentOutOfRangeException(nameof(attended));
        EndedOn = endedOn;
        EndedSessionsAttended = attended;
        EndedSessionsTotal = total;
        EndedValue = TuitionMath.Prorate(ListAmount - DiscountAmount - AdjustmentAmount, attended, total);
        var kept = Math.Min(paid, Amount);
        WaivedAmount = Amount - kept;
        Amount = kept;
        Version++;
    }

    /// <summary>The discounts changed for a cycle that is still running (never after leaving).</summary>
    public bool Reprice(IEnumerable<TuitionDiscount> discounts)
    {
        if (EndedOn is not null) return false;
        var discount = TuitionDiscount.Total(discounts.Where(x => x.AppliesTo(PeriodStart)), ListAmount);
        if (discount == DiscountAmount) return false;
        DiscountAmount = discount;
        Amount = Math.Max(0, ListAmount - discount - AdjustmentAmount);
        Version++;
        return true;
    }

    /// <summary>
    /// H18: the center chose to reduce this cycle for missed sessions (never automatic). Replaces any earlier
    /// reduction; zero removes it. A reason is always kept.
    /// </summary>
    public void Adjust(long amount, int? sessions, string? reason, Guid actorId, DateTimeOffset now)
    {
        if (EndedOn is not null) throw new InvalidOperationException("The cycle ended early.");
        var before = ListAmount - DiscountAmount;
        if (amount < 0 || amount > before || amount % 1000 != 0 || sessions is < 0 or > 100 || actorId == Guid.Empty)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (amount > 0 && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > MaxReasonLength))
            throw new ArgumentException("A reason is required.");
        AdjustmentAmount = amount;
        AdjustmentSessions = amount == 0 ? null : sessions;
        AdjustmentReason = amount == 0 ? null : reason!.Trim();
        AdjustedAtUtc = now;
        AdjustedByUserId = actorId;
        Amount = before - amount;
        Version++;
    }

    /// <summary>A merged profile keeps its enrollments; the charge follows them to the kept profile.</summary>
    public void MoveToStudent(Guid studentId)
    {
        if (studentId == Guid.Empty || studentId == StudentId) return;
        StudentId = studentId;
        Version++;
    }

    public void MarkReminded(DateTimeOffset now) => RemindedAtUtc ??= now;
}
