using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payroll;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Payroll;

/// <summary>What one teacher earned in one month, before manual lines and carried differences.</summary>
public sealed class EarnedFigures(Guid teacherId)
{
    public Guid TeacherId { get; } = teacherId;
    public int Sessions { get; set; }
    public int Minutes { get; set; }
    /// <summary>Share (M1) or sessions × rate (M2), whole VND.</summary>
    public long Base { get; set; }
    public decimal RawShare { get; set; }
    public Dictionary<Guid, ClassFigures> Classes { get; } = [];
    public List<SessionFigure> Items { get; } = [];

    public ClassFigures Class(Guid classId)
    {
        if (!Classes.TryGetValue(classId, out var row)) Classes[classId] = row = new ClassFigures(classId);
        return row;
    }
}

public sealed class ClassFigures(Guid classId)
{
    public Guid ClassId { get; } = classId;
    public int Sessions { get; set; }
    public int Minutes { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>A taught session counted in the month; <see cref="Amount"/> is its pay in M2, null when no rate is set.</summary>
public sealed record SessionFigure(Guid SessionId, Guid ClassId, DateTimeOffset StartUtc, int Minutes, long? Amount, bool Substitute);

/// <summary>G5: money paid into a class cycle in which nobody can be credited for teaching yet.</summary>
public sealed record UnassignedMoney(Guid ClassId, DateOnly PeriodStart, DateOnly PeriodEnd, long Amount);

public sealed class MonthFigures
{
    public required DateOnly Month { get; init; }
    public required PayrollMode Mode { get; init; }
    public Dictionary<Guid, EarnedFigures> Teachers { get; } = [];
    /// <summary>C5: sessions that took place this month but have no submitted sheet yet.</summary>
    public List<PayrollSessionRow> Unsubmitted { get; } = [];
    /// <summary>L9: M2 sessions with no rate in force.</summary>
    public List<SessionFigure> MissingRates { get; } = [];
    public List<UnassignedMoney> Unassigned { get; } = [];
    public long Collected { get; set; }
    public long Prepaid { get; set; }
    public long UnassignedTotal => Unassigned.Sum(x => x.Amount);
    public long Expenses { get; set; }
    public long Shareable { get; set; }
    public long Distributable => Shareable - Expenses;

    public EarnedFigures Teacher(Guid teacherId)
    {
        if (!Teachers.TryGetValue(teacherId, out var row)) Teachers[teacherId] = row = new EarnedFigures(teacherId);
        return row;
    }

    public long BaseOf(Guid teacherId) => Teachers.TryGetValue(teacherId, out var row) ? row.Base : 0;
}

/// <summary>Effective switch for a month: the latest configuration change in force on its first day (C2).</summary>
public sealed record MonthSetting(bool Enabled, PayrollMode Mode);

/// <summary>
/// Everything the payroll of one center is computed from, loaded once per request. Figures are pure functions of
/// these inputs and memoised per month; draft months are always recomputed, closed months keep their snapshot and
/// their recomputation only feeds the C9 differences.
/// </summary>
public sealed class PayrollBook
{
    private readonly Dictionary<DateOnly, MonthFigures> _figures = [];
    private readonly ILookup<Guid, PayrollSessionRow> _sessionsByClass;

    public PayrollBook(Guid centerId, DateTimeOffset now, IReadOnlyList<PayrollConfigChange> changes,
        IReadOnlyList<PayrollTeacherRow> teachers, IReadOnlyList<PayrollClassRow> classes,
        IReadOnlyList<PayrollSessionRow> sessions, IReadOnlyList<PayRate> rates, IReadOnlyList<PayrollAdjustment> adjustments,
        IReadOnlyList<SharedExpense> expenses, IReadOnlyList<PayrollPayout> payouts, IReadOnlyList<PayrollDispute> disputes,
        IReadOnlyList<PayrollClosedMonth> closed, IReadOnlyList<PayrollCarry> carries, IReadOnlyList<PayrollMonthEvent> events,
        IReadOnlyList<Receipt> receipts, IReadOnlyList<TuitionCharge> charges)
    {
        CenterId = centerId;
        Now = now;
        Today = FinanceTime.LocalDate(now);
        Changes = changes.OrderBy(x => x.CreatedAtUtc).ToArray();
        Teachers = teachers.ToDictionary(x => x.Id);
        Classes = classes.ToDictionary(x => x.Id);
        Sessions = sessions;
        _sessionsByClass = sessions.ToLookup(x => x.ClassId);
        Rates = rates;
        Adjustments = adjustments;
        Expenses = expenses;
        Payouts = payouts;
        Disputes = disputes;
        Closed = closed.ToDictionary(x => x.Month);
        Carries = carries;
        Events = events;
        Receipts = receipts;
        Charges = charges.ToDictionary(x => x.Id);
    }

    public Guid CenterId { get; }
    public DateTimeOffset Now { get; }
    public DateOnly Today { get; }
    public DateOnly CurrentMonth => PayMonth.Of(Today);
    public IReadOnlyList<PayrollConfigChange> Changes { get; }
    public IReadOnlyDictionary<Guid, PayrollTeacherRow> Teachers { get; }
    public IReadOnlyDictionary<Guid, PayrollClassRow> Classes { get; }
    public IReadOnlyList<PayrollSessionRow> Sessions { get; }
    public IReadOnlyList<PayRate> Rates { get; }
    public IReadOnlyList<PayrollAdjustment> Adjustments { get; }
    public IReadOnlyList<SharedExpense> Expenses { get; }
    public IReadOnlyList<PayrollPayout> Payouts { get; }
    public IReadOnlyList<PayrollDispute> Disputes { get; }
    public IReadOnlyDictionary<DateOnly, PayrollClosedMonth> Closed { get; }
    public IReadOnlyList<PayrollCarry> Carries { get; }
    public IReadOnlyList<PayrollMonthEvent> Events { get; }
    public IReadOnlyList<Receipt> Receipts { get; }
    public IReadOnlyDictionary<Guid, TuitionCharge> Charges { get; }

    // ---------------- scope ----------------

    public MonthSetting SettingOf(DateOnly month)
    {
        var change = Changes.LastOrDefault(x => x.EffectiveFrom <= month);
        return change is null ? new MonthSetting(false, PayrollMode.RevenueShare) : new MonthSetting(change.Enabled, change.Mode);
    }

    /// <summary>The mode a month is computed with: frozen at closing, else the setting in force.</summary>
    public PayrollMode ModeOf(DateOnly month) => Closed.TryGetValue(month, out var closed) ? closed.Mode : SettingOf(month).Mode;

    public bool IsEnabledNow => SettingOf(CurrentMonth).Enabled;

    /// <summary>Months the module was on, from the first enabled month to this month, newest first.</summary>
    public IReadOnlyList<DateOnly> MonthsInScope()
    {
        var first = Changes.Where(x => x.Enabled).Select(x => (DateOnly?)x.EffectiveFrom).Min();
        if (first is null || first > CurrentMonth) return [];
        var months = new List<DateOnly>();
        for (var month = CurrentMonth; month >= first; month = month.AddMonths(-1))
            if (SettingOf(month).Enabled || Closed.ContainsKey(month)) months.Add(month);
        return months;
    }

    public bool IsClosed(DateOnly month) => Closed.ContainsKey(month);

    // ---------------- figures ----------------

    public MonthFigures Figures(DateOnly month)
    {
        if (_figures.TryGetValue(month, out var cached)) return cached;
        var figures = new MonthFigures { Month = month, Mode = ModeOf(month) };
        var end = PayMonth.End(month);
        foreach (var session in Sessions)
        {
            var day = FinanceTime.LocalDate(session.StartUtc);
            if (day < month || day > end || !session.IsActive) continue;
            if (!session.Submitted || session.ActualTeacherId is null)
            {
                if (session.StartUtc <= Now) figures.Unsubmitted.Add(session);
                continue;
            }

            var teacher = figures.Teacher(session.ActualTeacherId.Value);
            teacher.Sessions++;
            teacher.Minutes += session.Minutes;
            var cls = teacher.Class(session.ClassId);
            cls.Sessions++;
            cls.Minutes += session.Minutes;
            long? amount = null;
            if (figures.Mode == PayrollMode.PerSession)
            {
                var rate = PayRate.For(Rates, session.ActualTeacherId.Value, session.ClassId, day);
                amount = rate?.Amount;
                if (amount is { } value)
                {
                    teacher.Base += value;
                    cls.Amount += value;
                }
            }

            var item = new SessionFigure(session.Id, session.ClassId, session.StartUtc, session.Minutes, amount,
                session.ActualTeacherId != session.TeacherId);
            teacher.Items.Add(item);
            if (figures.Mode == PayrollMode.PerSession && amount is null) figures.MissingRates.Add(item);
        }

        if (figures.Mode == PayrollMode.RevenueShare) Share(figures);
        _figures[month] = figures;
        return figures;
    }

    /// <summary>G1–G7: money collected in the month, by class cycle, by minutes taught in that cycle.</summary>
    private void Share(MonthFigures figures)
    {
        var end = PayMonth.End(figures.Month);
        foreach (var receipt in Receipts.Where(x => !x.IsVoided && x.ReceivedOn >= figures.Month && x.ReceivedOn <= end))
        {
            figures.Collected += receipt.Amount;
            figures.Prepaid += receipt.Unallocated;
            foreach (var allocation in receipt.Allocations)
            {
                if (allocation.Amount <= 0 || !Charges.TryGetValue(allocation.ChargeId, out var charge)) continue;
                var weights = CycleWeights(charge.ClassId, charge.PeriodStart, charge.PeriodEnd);
                var total = weights.Values.Sum();
                if (total == 0)
                {
                    figures.Unassigned.Add(new UnassignedMoney(charge.ClassId, charge.PeriodStart, charge.PeriodEnd, allocation.Amount));
                    continue;
                }

                foreach (var (teacherId, minutes) in weights)
                {
                    var part = allocation.Amount * (decimal)minutes / total;
                    var teacher = figures.Teacher(teacherId);
                    teacher.RawShare += part;
                    teacher.Class(charge.ClassId).Amount += part;
                }
            }
        }

        figures.Expenses = Expenses.Where(x => !x.IsVoided && x.Month == figures.Month).Sum(x => x.Amount);
        var raw = figures.Teachers.Values.Sum(x => x.RawShare);
        figures.Shareable = (long)Math.Round(raw, MidpointRounding.AwayFromZero);
        if (raw <= 0) return;

        // G4: costs come off in the same proportions; G7: thousands each, the rest to the largest share.
        var factor = (decimal)figures.Distributable / raw;
        var sharers = figures.Teachers.Values.Where(x => x.RawShare > 0).OrderByDescending(x => x.RawShare).ToArray();
        long given = 0;
        foreach (var teacher in sharers.Skip(1))
        {
            teacher.Base = (long)Math.Truncate(teacher.RawShare * factor / 1000m) * 1000;
            given += teacher.Base;
        }

        sharers[0].Base = figures.Distributable - given;
        foreach (var teacher in sharers)
        foreach (var cls in teacher.Classes.Values)
            cls.Amount = Math.Round(cls.Amount * factor, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// G3: minutes per teacher in one class cycle. Taught sessions count for who actually taught; sessions still ahead
    /// count for who is assigned (an estimate that follows reality once taught); past sessions without a sheet wait.
    /// </summary>
    private Dictionary<Guid, int> CycleWeights(Guid classId, DateOnly start, DateOnly end)
    {
        var weights = new Dictionary<Guid, int>();
        foreach (var session in _sessionsByClass[classId])
        {
            if (!session.IsActive) continue;
            var day = FinanceTime.LocalDate(session.StartUtc);
            if (day < start || day > end) continue;
            Guid? teacher = session.Submitted ? session.ActualTeacherId : session.StartUtc > Now ? session.TeacherId : null;
            if (teacher is null) continue;
            weights[teacher.Value] = weights.GetValueOrDefault(teacher.Value) + session.Minutes;
        }

        return weights;
    }

    // ---------------- month lines ----------------

    public long AdjustmentsOf(DateOnly month, Guid teacherId) =>
        Adjustments.Where(x => !x.IsVoided && x.Month == month && x.TeacherId == teacherId).Sum(x => x.Amount);

    public long PaidOf(DateOnly month, Guid teacherId) =>
        Payouts.Where(x => !x.IsVoided && x.Month == month && x.TeacherId == teacherId).Sum(x => x.Amount);

    /// <summary>
    /// C9 for a draft month: for every closed month before it, recomputed base − frozen base − what was already
    /// carried out of it. For a closed month: what was booked into it when it was closed.
    /// </summary>
    public IReadOnlyList<(DateOnly From, Guid TeacherId, long Amount)> CarriedInto(DateOnly month)
    {
        if (Closed.ContainsKey(month))
            return Carries.Where(x => x.IntoMonth == month).Select(x => (x.FromMonth, x.TeacherId, x.Amount)).ToArray();

        var result = new List<(DateOnly, Guid, long)>();
        foreach (var closed in Closed.Values.Where(x => x.Month < month).OrderBy(x => x.Month))
        {
            var live = Figures(closed.Month);
            var teachers = live.Teachers.Keys.Concat(closed.Lines.Select(x => x.TeacherId)).Distinct();
            foreach (var teacherId in teachers)
            {
                var frozen = closed.Lines.FirstOrDefault(x => x.TeacherId == teacherId)?.Base ?? 0;
                var already = Carries.Where(x => x.FromMonth == closed.Month && x.TeacherId == teacherId).Sum(x => x.Amount);
                var difference = live.BaseOf(teacherId) - frozen - already;
                if (difference != 0) result.Add((closed.Month, teacherId, difference));
            }
        }

        return result;
    }

    public sealed record Line(Guid TeacherId, int Sessions, int Minutes, long Base, long Adjustments, long Carried, long Paid)
    {
        public long Net => Base + Adjustments + Carried;
        public long Remaining => Net - Paid;
    }

    /// <summary>Every teacher with anything in the month: work, money, manual lines, payouts or a report.</summary>
    public IReadOnlyList<Line> LinesOf(DateOnly month)
    {
        if (Closed.TryGetValue(month, out var closed))
        {
            var frozen = closed.Lines.ToDictionary(x => x.TeacherId);
            var ids = frozen.Keys.Concat(Payouts.Where(x => !x.IsVoided && x.Month == month).Select(x => x.TeacherId)).Distinct();
            return ids.Select(id => frozen.TryGetValue(id, out var line)
                    ? new Line(id, line.Sessions, line.Minutes, line.Base, line.Adjustments, line.Carried, PaidOf(month, id))
                    : new Line(id, 0, 0, 0, 0, 0, PaidOf(month, id)))
                .ToArray();
        }

        var figures = Figures(month);
        var carried = CarriedInto(month);
        var teacherIds = figures.Teachers.Keys
            .Concat(carried.Select(x => x.TeacherId))
            .Concat(Adjustments.Where(x => !x.IsVoided && x.Month == month).Select(x => x.TeacherId))
            .Concat(Payouts.Where(x => !x.IsVoided && x.Month == month).Select(x => x.TeacherId))
            .Concat(Disputes.Where(x => x.Month == month).Select(x => x.TeacherId))
            .Distinct();
        return teacherIds.Select(id =>
        {
            figures.Teachers.TryGetValue(id, out var earned);
            return new Line(id, earned?.Sessions ?? 0, earned?.Minutes ?? 0, earned?.Base ?? 0, AdjustmentsOf(month, id),
                carried.Where(x => x.TeacherId == id).Sum(x => x.Amount), PaidOf(month, id));
        }).ToArray();
    }

    // ---------------- closing rules ----------------

    /// <summary>Why the month cannot be closed now; empty when it can (C8, C14, L9).</summary>
    public IReadOnlyList<string> CloseBlockers(DateOnly month)
    {
        var blockers = new List<string>();
        if (Closed.ContainsKey(month)) return ["already_closed"];
        if (month > CurrentMonth) blockers.Add("future_month");
        if (!SettingOf(month).Enabled) blockers.Add("disabled");
        if (MonthsInScope().Any(x => x < month && !Closed.ContainsKey(x))) blockers.Add("earlier_month_open");
        if (Disputes.Any(x => x.Month == month && x.IsOpen)) blockers.Add("open_disputes");
        var figures = Figures(month);
        if (figures.MissingRates.Count > 0) blockers.Add("missing_rates");
        if (figures.Mode == PayrollMode.RevenueShare && figures.Distributable < 0) blockers.Add("expenses_exceed_income");
        var lines = LinesOf(month);
        if (lines.Any(x => x.Net < 0)) blockers.Add("negative_net");
        if (lines.Any(x => x.Paid > x.Net)) blockers.Add("overpaid");
        return blockers;
    }

    /// <summary>The month that may be reopened: the latest closed one.</summary>
    public DateOnly? LatestClosed => Closed.Count == 0 ? null : Closed.Keys.Max();
}
