using System.Globalization;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payroll;

/// <summary>docs/LUONG-VA-CHIA-THU-NHAP.md: how a center pays the people who teach.</summary>
public enum PayrollMode
{
    /// <summary>M1: tuition actually collected is shared among the teachers of each class (family business).</summary>
    RevenueShare = 1,

    /// <summary>M2: sessions taught × a per-session rate.</summary>
    PerSession = 2
}

/// <summary>Calendar months on Vietnam time (C4), kept as the first day of the month.</summary>
public static class PayMonth
{
    public static DateOnly Of(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly End(DateOnly month) => month.AddMonths(1).AddDays(-1);

    public static string Format(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static bool TryParse(string? text, out DateOnly month)
    {
        month = default;
        if (text is null || !DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed)) return false;
        month = parsed;
        return true;
    }

    public static bool IsMonthStart(DateOnly date) => date.Day == 1;
}

public static class PayrollText
{
    public const int MaxReason = 300;
    public const int MaxCategory = 60;

    public static bool IsReason(string? value) => value is not null && value.Trim().Length is >= 3 and <= MaxReason;

    public static bool IsThousands(long amount) => amount % 1000 == 0;
}

/// <summary>C1–C3: per-center switch, set only by the platform admin. Missing row = off.</summary>
public sealed class PayrollConfig
{
    private PayrollConfig() { }

    public Guid CenterId { get; private set; }
    public bool Enabled { get; private set; }
    public PayrollMode Mode { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public int Version { get; private set; }

    public static PayrollConfig Create(Guid centerId) => new() { CenterId = centerId, Mode = PayrollMode.RevenueShare };

    public PayrollConfigChange Change(bool enabled, PayrollMode mode, DateOnly effectiveFrom, string reason, Guid actorId,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode) || !PayMonth.IsMonthStart(effectiveFrom) || !PayrollText.IsReason(reason))
            throw new ArgumentException("Payroll configuration is invalid.");
        var change = new PayrollConfigChange(CenterId, Enabled, Mode, enabled, mode, effectiveFrom, reason.Trim(), actorId, now);
        Enabled = enabled;
        Mode = mode;
        EffectiveFrom = effectiveFrom;
        UpdatedAtUtc = now;
        UpdatedByUserId = actorId;
        Version++;
        return change;
    }
}

/// <summary>C3: every switch, with the reason.</summary>
public sealed class PayrollConfigChange : Entity<Guid>
{
    private PayrollConfigChange() : base(Guid.Empty) { }

    internal PayrollConfigChange(Guid centerId, bool previousEnabled, PayrollMode previousMode, bool enabled, PayrollMode mode,
        DateOnly effectiveFrom, string reason, Guid actorId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        PreviousEnabled = previousEnabled;
        PreviousMode = previousMode;
        Enabled = enabled;
        Mode = mode;
        EffectiveFrom = effectiveFrom;
        Reason = reason;
        ActorUserId = actorId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public bool PreviousEnabled { get; private set; }
    public PayrollMode PreviousMode { get; private set; }
    public bool Enabled { get; private set; }
    public PayrollMode Mode { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}

/// <summary>Base class for the center's own entries that can be voided with a reason but never deleted.</summary>
public abstract class VoidablePayrollEntry : Entity<Guid>
{
    protected VoidablePayrollEntry() : base(Guid.Empty) { }

    protected VoidablePayrollEntry(Guid centerId, Guid createdByUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; protected set; }
    public Guid CreatedByUserId { get; protected set; }
    public DateTimeOffset CreatedAtUtc { get; protected set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public bool IsVoided => VoidedAtUtc is not null;

    public void Void(string reason, Guid actorId, DateTimeOffset now)
    {
        if (IsVoided) throw new InvalidOperationException("Already void.");
        if (!PayrollText.IsReason(reason)) throw new ArgumentException("A reason is required.");
        VoidedAtUtc = now;
        VoidedByUserId = actorId;
        VoidReason = reason.Trim();
    }
}

/// <summary>L1–L3: VND per session, for a teacher (default) or a teacher in one class (override), from a date.</summary>
public sealed class PayRate : VoidablePayrollEntry
{
    public const long MaxAmount = 50_000_000;

    private PayRate() { }

    private PayRate(Guid centerId, Guid teacherId, Guid? classId, long amount, DateOnly effectiveFrom, Guid actorId,
        DateTimeOffset now) : base(centerId, actorId, now)
    {
        TeacherId = teacherId;
        ClassId = classId;
        Amount = amount;
        EffectiveFrom = effectiveFrom;
    }

    public Guid TeacherId { get; private set; }
    public Guid? ClassId { get; private set; }
    public long Amount { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }

    public static bool IsValidAmount(long amount) => amount is >= 0 and <= MaxAmount && PayrollText.IsThousands(amount);

    public static PayRate Create(Guid centerId, Guid teacherId, Guid? classId, long amount, DateOnly effectiveFrom,
        Guid actorId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || teacherId == Guid.Empty || classId == Guid.Empty || !IsValidAmount(amount))
            throw new ArgumentException("Pay rate is invalid.");
        return new(centerId, teacherId, classId, amount, effectiveFrom, actorId, now);
    }

    /// <summary>L2/L3: the class override wins over the default; within each, the latest rate in force that day.</summary>
    public static PayRate? For(IEnumerable<PayRate> rates, Guid teacherId, Guid classId, DateOnly day)
    {
        var live = rates.Where(x => !x.IsVoided && x.TeacherId == teacherId && x.EffectiveFrom <= day).ToArray();
        return live.Where(x => x.ClassId == classId).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedAtUtc)
                   .FirstOrDefault()
            ?? live.Where(x => x.ClassId is null).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedAtUtc)
                   .FirstOrDefault();
    }
}

/// <summary>C10: a manual plus or minus on one teacher's month.</summary>
public sealed class PayrollAdjustment : VoidablePayrollEntry
{
    public const long MaxAmount = 100_000_000;

    private PayrollAdjustment() { }

    private PayrollAdjustment(Guid centerId, DateOnly month, Guid teacherId, long amount, string reason, Guid actorId,
        DateTimeOffset now) : base(centerId, actorId, now)
    {
        Month = month;
        TeacherId = teacherId;
        Amount = amount;
        Reason = reason;
    }

    public DateOnly Month { get; private set; }
    public Guid TeacherId { get; private set; }
    public long Amount { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    public static bool IsValidAmount(long amount) =>
        amount != 0 && Math.Abs(amount) <= MaxAmount && PayrollText.IsThousands(amount);

    public static PayrollAdjustment Create(Guid centerId, DateOnly month, Guid teacherId, long amount, string reason,
        Guid actorId, DateTimeOffset now)
    {
        if (teacherId == Guid.Empty || !PayMonth.IsMonthStart(month) || !IsValidAmount(amount) || !PayrollText.IsReason(reason))
            throw new ArgumentException("Adjustment is invalid.");
        return new(centerId, month, teacherId, amount, reason.Trim(), actorId, now);
    }
}

/// <summary>G4: a shared cost (rent, power, books) taken off the month's money before it is shared (M1 only).</summary>
public sealed class SharedExpense : VoidablePayrollEntry
{
    private SharedExpense() { }

    private SharedExpense(Guid centerId, DateOnly month, DateOnly spentOn, string category, long amount, string note,
        Guid actorId, DateTimeOffset now) : base(centerId, actorId, now)
    {
        Month = month;
        SpentOn = spentOn;
        Category = category;
        Amount = amount;
        Note = note;
    }

    public DateOnly Month { get; private set; }
    public DateOnly SpentOn { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public string Note { get; private set; } = string.Empty;

    public static bool IsValid(DateOnly month, DateOnly spentOn, string? category, long amount, string? note) =>
        PayMonth.Of(spentOn) == month && category is not null && category.Trim().Length is >= 2 and <= PayrollText.MaxCategory &&
        amount is > 0 and <= 10 * TuitionMath.MaxFee && PayrollText.IsReason(note);

    public static SharedExpense Create(Guid centerId, DateOnly month, DateOnly spentOn, string category, long amount,
        string note, Guid actorId, DateTimeOffset now)
    {
        if (!IsValid(month, spentOn, category, amount, note)) throw new ArgumentException("Expense is invalid.");
        return new(centerId, month, spentOn, category.Trim(), amount, note.Trim(), actorId, now);
    }
}

/// <summary>C11: money handed to a teacher, numbered per center (PC-000001). Allowed on a draft month as an advance (L7).</summary>
public sealed class PayrollPayout : VoidablePayrollEntry
{
    public const int MaxNote = 300;

    private PayrollPayout() { }

    private PayrollPayout(Guid centerId, int number, DateOnly month, Guid teacherId, long amount, PaymentMethod method,
        DateOnly paidOn, string? note, Guid? requestId, Guid actorId, DateTimeOffset now) : base(centerId, actorId, now)
    {
        Number = number;
        Month = month;
        TeacherId = teacherId;
        Amount = amount;
        Method = method;
        PaidOn = paidOn;
        Note = note;
        RequestId = requestId;
    }

    public int Number { get; private set; }
    public DateOnly Month { get; private set; }
    public Guid TeacherId { get; private set; }
    public long Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public DateOnly PaidOn { get; private set; }
    public string? Note { get; private set; }
    public Guid? RequestId { get; private set; }

    public static string FormatNumber(int number) => $"PC-{number:000000}";

    public static bool IsValid(long amount, PaymentMethod method, string? note) =>
        amount is > 0 and <= PayrollAdjustment.MaxAmount && Enum.IsDefined(method) &&
        (note is null || note.Trim().Length <= MaxNote);

    public static PayrollPayout Create(Guid centerId, int number, DateOnly month, Guid teacherId, long amount,
        PaymentMethod method, DateOnly paidOn, string? note, Guid? requestId, Guid actorId, DateTimeOffset now)
    {
        if (number <= 0 || teacherId == Guid.Empty || !PayMonth.IsMonthStart(month) || !IsValid(amount, method, note))
            throw new ArgumentException("Payout is invalid.");
        return new(centerId, number, month, teacherId, amount, method, paidOn,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(), requestId, actorId, now);
    }
}

/// <summary>C14: a teacher says their draft figures are wrong; must be resolved before the month is closed.</summary>
public sealed class PayrollDispute : Entity<Guid>
{
    private PayrollDispute() : base(Guid.Empty) { }

    private PayrollDispute(Guid centerId, DateOnly month, Guid teacherId, string message, Guid userId, DateTimeOffset now)
        : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Month = month;
        TeacherId = teacherId;
        Message = message;
        CreatedByUserId = userId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public DateOnly Month { get; private set; }
    public Guid TeacherId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? Resolution { get; private set; }
    public bool IsOpen => ResolvedAtUtc is null;

    public static PayrollDispute Create(Guid centerId, DateOnly month, Guid teacherId, string message, Guid userId,
        DateTimeOffset now)
    {
        if (!PayrollText.IsReason(message)) throw new ArgumentException("A message is required.");
        return new(centerId, month, teacherId, message.Trim(), userId, now);
    }

    public void Resolve(string note, Guid actorId, DateTimeOffset now)
    {
        if (!IsOpen) throw new InvalidOperationException("Already resolved.");
        if (!PayrollText.IsReason(note)) throw new ArgumentException("A note is required.");
        ResolvedAtUtc = now;
        ResolvedByUserId = actorId;
        Resolution = note.Trim();
    }
}

/// <summary>C8: a closed month with the figures frozen at closing. Reopening removes it (the events keep the history).</summary>
public sealed class PayrollClosedMonth : Entity<Guid>
{
    private readonly List<PayrollMonthLine> _lines = [];

    private PayrollClosedMonth() : base(Guid.Empty) { }

    private PayrollClosedMonth(Guid centerId, DateOnly month, PayrollMode mode, Guid actorId, DateTimeOffset now)
        : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Month = month;
        Mode = mode;
        ClosedByUserId = actorId;
        ClosedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public DateOnly Month { get; private set; }
    public PayrollMode Mode { get; private set; }
    public Guid ClosedByUserId { get; private set; }
    public DateTimeOffset ClosedAtUtc { get; private set; }
    public IReadOnlyList<PayrollMonthLine> Lines => _lines;

    public static PayrollClosedMonth Create(Guid centerId, DateOnly month, PayrollMode mode, Guid actorId, DateTimeOffset now,
        IEnumerable<PayrollMonthLine> lines)
    {
        var closed = new PayrollClosedMonth(centerId, month, mode, actorId, now);
        closed._lines.AddRange(lines);
        return closed;
    }
}

/// <summary>One teacher's frozen figures in a closed month. <see cref="Base"/> is what C9 compares against.</summary>
public sealed class PayrollMonthLine
{
    private PayrollMonthLine() { }

    public PayrollMonthLine(Guid teacherId, int sessions, int minutes, long @base, long adjustments, long carried)
    {
        TeacherId = teacherId;
        Sessions = sessions;
        Minutes = minutes;
        Base = @base;
        Adjustments = adjustments;
        Carried = carried;
    }

    public Guid TeacherId { get; private set; }
    public int Sessions { get; private set; }
    public int Minutes { get; private set; }
    /// <summary>The earned part: the share (M1) or sessions × rate (M2).</summary>
    public long Base { get; private set; }
    public long Adjustments { get; private set; }
    public long Carried { get; private set; }
    public long Net => Base + Adjustments + Carried;
}

/// <summary>C9: a difference found in a closed month and booked into a later one when that one was closed.</summary>
public sealed class PayrollCarry : Entity<Guid>
{
    private PayrollCarry() : base(Guid.Empty) { }

    public PayrollCarry(Guid centerId, DateOnly fromMonth, DateOnly intoMonth, Guid teacherId, long amount) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        FromMonth = fromMonth;
        IntoMonth = intoMonth;
        TeacherId = teacherId;
        Amount = amount;
    }

    public Guid CenterId { get; private set; }
    public DateOnly FromMonth { get; private set; }
    public DateOnly IntoMonth { get; private set; }
    public Guid TeacherId { get; private set; }
    public long Amount { get; private set; }
}

public enum PayrollMonthEventKind
{
    Closed = 1,
    Reopened = 2
}

/// <summary>C8: who closed or reopened a month, and why.</summary>
public sealed class PayrollMonthEvent : Entity<Guid>
{
    private PayrollMonthEvent() : base(Guid.Empty) { }

    public PayrollMonthEvent(Guid centerId, DateOnly month, PayrollMonthEventKind kind, string? reason, Guid actorId,
        DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Month = month;
        Kind = kind;
        Reason = reason;
        ActorUserId = actorId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public DateOnly Month { get; private set; }
    public PayrollMonthEventKind Kind { get; private set; }
    public string? Reason { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
}
