using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>
/// Everything tuition knows about one center, loaded at once: Academics facts (classes, sessions, enrollments,
/// students) and the finance rows. <see cref="Sync"/> bills the cycles that have started (docs/HOC-PHI.md).
/// Load it inside the center guard before changing anything.
/// </summary>
public sealed class CenterLedger
{
    private readonly Dictionary<Guid, TuitionStudentRow> _students;
    private readonly ILookup<Guid, TuitionSessionRow> _sessions;
    private readonly ILookup<Guid, TuitionMissedSessionRow> _missed;
    private readonly ILookup<Guid, TuitionAbsenceRow> _absences;
    private readonly List<ClassBillingAnchor> _newAnchors = [];
    private readonly List<TuitionCharge> _newCharges = [];

    private CenterLedger(Guid centerId, IReadOnlyList<TuitionClassRow> classes, List<ClassFee> fees,
        IReadOnlyList<ClassBillingAnchor> anchors, IReadOnlyList<TuitionSessionRow> sessions,
        IReadOnlyList<TuitionEnrollmentRow> enrollments, IReadOnlyList<TuitionStudentRow> students,
        IReadOnlyList<TuitionMissedSessionRow> missed, IReadOnlyList<TuitionAbsenceRow> absences,
        List<TuitionCharge> charges, List<TuitionDiscount> discounts, List<Receipt> receipts, FinanceSettings settings)
    {
        CenterId = centerId;
        Classes = classes.ToDictionary(x => x.Id);
        Fees = fees;
        Anchors = anchors.ToDictionary(x => x.ClassId, x => x.AnchorDate);
        _sessions = sessions.OrderBy(x => x.StartUtc).ToLookup(x => x.ClassId);
        Enrollments = enrollments;
        _students = students.ToDictionary(x => x.Id);
        _missed = missed.OrderBy(x => x.StartUtc).ToLookup(x => x.ClassId);
        _absences = absences.OrderBy(x => x.StartUtc).ToLookup(x => x.EnrollmentId);
        Charges = charges;
        Discounts = discounts;
        Receipts = receipts;
        Settings = settings;
    }

    public Guid CenterId { get; }
    public IReadOnlyDictionary<Guid, TuitionClassRow> Classes { get; }
    public List<ClassFee> Fees { get; }
    public Dictionary<Guid, DateOnly> Anchors { get; }
    public IReadOnlyList<TuitionEnrollmentRow> Enrollments { get; }
    public List<TuitionCharge> Charges { get; }
    public List<TuitionDiscount> Discounts { get; }
    public List<Receipt> Receipts { get; }
    public FinanceSettings Settings { get; }
    public IReadOnlyList<ClassBillingAnchor> NewAnchors => _newAnchors;
    public IReadOnlyList<TuitionCharge> NewCharges => _newCharges;

    public static async Task<CenterLedger> LoadAsync(Guid centerId, ITuitionStore store, ITuitionAcademicsReader reader,
        CancellationToken token)
    {
        var classes = await reader.ListClassesAsync(centerId, token);
        var fees = await store.ListFeesAsync(centerId, token);
        // Every class, not only billed ones: setting a first fee bills the running cycle in the same call.
        var billed = classes.Select(x => x.Id).ToArray();
        var sessions = billed.Length == 0 ? [] : await reader.ListSessionsAsync(centerId, billed, token);
        var enrollments = await reader.ListEnrollmentsAsync(centerId, classes.Select(x => x.Id).ToArray(), token);
        var students = await reader.ListStudentsAsync(centerId, token);
        var missed = billed.Length == 0 ? [] : await reader.ListMissedSessionsAsync(centerId, billed, token);
        var absences = billed.Length == 0 ? [] : await reader.ListAbsencesAsync(centerId, billed, token);
        return new CenterLedger(centerId, classes, [.. fees], await store.ListAnchorsAsync(centerId, token), sessions,
            enrollments, students, missed, absences, [.. await store.ListChargesAsync(centerId, token)],
            [.. await store.ListDiscountsAsync(centerId, token)], [.. await store.ListReceiptsAsync(centerId, token)],
            await store.FindSettingsAsync(centerId, token) ?? FinanceSettings.Default(centerId));
    }

    public TuitionStudentRow? FindStudent(Guid studentId) => _students.GetValueOrDefault(studentId);

    public IEnumerable<TuitionStudentRow> Students => _students.Values;

    /// <summary>The profile that a merged profile now lives in (M3), or the profile itself.</summary>
    public Guid Resolve(Guid studentId)
    {
        var current = studentId;
        for (var hop = 0; hop < 10 && _students.TryGetValue(current, out var row) && row.MergedIntoStudentId is { } next; hop++)
            current = next;
        return current;
    }

    public IReadOnlyList<ClassFee> FeeHistory(Guid classId) => Fees.Where(x => x.ClassId == classId).ToArray();

    public IEnumerable<TuitionSessionRow> SessionsOf(Guid classId) => _sessions[classId];

    /// <summary>
    /// H18: the sessions of a charge's cycle the student did not get — their own absences on submitted sheets and
    /// sessions the center cancelled or did not hold — shown so the center can decide on a reduction.
    /// </summary>
    public IReadOnlyList<MissedSessionDto> MissedFor(TuitionCharge charge)
    {
        var enrollment = Enrollments.FirstOrDefault(x => x.Id == charge.EnrollmentId);
        var from = enrollment?.EnrolledAtUtc ?? DateTimeOffset.MinValue;
        var until = enrollment?.WithdrawnAtUtc ?? DateTimeOffset.MaxValue;
        bool InCycle(DateTimeOffset start)
        {
            var day = FinanceTime.LocalDate(start);
            return day >= charge.PeriodStart && day <= charge.PeriodEnd && start >= from && start < until;
        }
        var items = _absences[charge.EnrollmentId].Where(x => InCycle(x.StartUtc))
            .Select(x => new MissedSessionDto { SessionId = x.SessionId, StartUtc = x.StartUtc, Kind = x.Mark })
            .Concat(_missed[charge.ClassId].Where(x => InCycle(x.StartUtc)).Select(x => new MissedSessionDto
            {
                SessionId = x.SessionId, StartUtc = x.StartUtc, Kind = x.Kind, Reason = x.Reason, HasMakeup = x.HasMakeup
            }));
        return items.OrderBy(x => x.StartUtc).ToArray();
    }

    /// <summary>Sessions of the cycle this student was billed for (the divisor of the per-session suggestion).</summary>
    public int BilledSessions(TuitionCharge charge)
    {
        if (charge.JoinSessions is { } joined) return joined;
        return InWindow(SessionsOf(charge.ClassId), charge.PeriodStart, charge.PeriodEnd).Length;
    }

    // ---------------- money ----------------

    public long PaidFor(Guid chargeId)
    {
        long paid = 0;
        foreach (var receipt in Receipts)
        {
            if (receipt.IsVoided) continue;
            foreach (var line in receipt.Allocations)
                if (line.ChargeId == chargeId) paid += line.Amount;
        }
        return paid;
    }

    public long Remaining(TuitionCharge charge) => Math.Max(0, charge.Amount - PaidFor(charge.Id));

    public long CreditOf(Guid studentId) =>
        Receipts.Where(x => x.StudentId == studentId && !x.IsVoided).Sum(x => x.Unallocated);

    public IEnumerable<TuitionCharge> OpenCharges(Guid studentId) => Charges
        .Where(x => x.StudentId == studentId && Remaining(x) > 0)
        .OrderBy(x => x.PeriodStart).ThenBy(x => x.DueDate).ThenBy(x => x.CreatedAtUtc);

    /// <summary>H11: spreads one receipt over the student's open charges, the chosen ones first, then the oldest.</summary>
    public void Spread(Receipt receipt, IReadOnlyList<Guid> firstChargeIds, DateTimeOffset now)
    {
        var open = OpenCharges(receipt.StudentId).ToList();
        var ordered = firstChargeIds.Select(id => open.FirstOrDefault(x => x.Id == id)).OfType<TuitionCharge>()
            .Concat(open.Where(x => !firstChargeIds.Contains(x.Id)));
        foreach (var charge in ordered)
        {
            if (receipt.Unallocated <= 0) break;
            receipt.Allocate(charge.Id, Remaining(charge), now);
        }
    }

    /// <summary>H11: credit left on older receipts pays charges billed later.</summary>
    public void SpreadCredit(Guid studentId, DateTimeOffset now)
    {
        foreach (var receipt in Receipts.Where(x => x.StudentId == studentId && x.Unallocated > 0)
                     .OrderBy(x => x.ReceivedOn).ThenBy(x => x.Number))
            Spread(receipt, [], now);
    }

    /// <summary>A charge now costs less than was spread over it: give the excess back as credit (newest money first).</summary>
    public void ReleaseExcess(TuitionCharge charge)
    {
        var excess = PaidFor(charge.Id) - charge.Amount;
        foreach (var receipt in Receipts.Where(x => !x.IsVoided && x.Allocations.Any(a => a.ChargeId == charge.Id))
                     .OrderByDescending(x => x.ReceivedOn).ThenByDescending(x => x.Number))
        {
            if (excess <= 0) break;
            excess -= receipt.Release(charge.Id, excess);
        }
    }

    public string ChargeStatus(TuitionCharge charge, DateOnly today)
    {
        var paid = PaidFor(charge.Id);
        if (paid >= charge.Amount) return "Paid";
        if (today > charge.DueDate) return "Overdue";
        return paid > 0 ? "Partial" : "Unpaid";
    }

    // ---------------- billing ----------------

    /// <summary>
    /// Bills every cycle that has started, closes the cycle a student left in (H7), follows merged profiles and then
    /// spreads available credit. Idempotent: an existing (enrollment, cycle) is never billed twice.
    /// </summary>
    public void Sync(DateOnly today, DateTimeOffset now)
    {
        var touched = new HashSet<Guid>();
        foreach (var classId in Fees.Select(x => x.ClassId).Distinct())
        {
            var sessions = SessionsOf(classId).ToArray();
            if (!Anchors.TryGetValue(classId, out var anchor))
            {
                if (sessions.Length == 0 || FinanceTime.LocalDate(sessions[0].StartUtc) > today) continue;
                anchor = FinanceTime.LocalDate(sessions[0].StartUtc);
                Anchors[classId] = anchor;
                _newAnchors.Add(ClassBillingAnchor.Create(CenterId, classId, anchor, now));
            }
            var history = FeeHistory(classId);
            foreach (var enrollment in Enrollments.Where(x => x.ClassId == classId))
                SyncEnrollment(enrollment, sessions, anchor, history, today, now, touched);
        }

        foreach (var charge in Charges)
        {
            var enrollment = Enrollments.FirstOrDefault(x => x.Id == charge.EnrollmentId);
            var owner = Resolve(enrollment?.StudentId ?? charge.StudentId);
            if (owner != charge.StudentId) { charge.MoveToStudent(owner); touched.Add(owner); }
        }
        foreach (var receipt in Receipts)
        {
            var owner = Resolve(receipt.StudentId);
            if (owner != receipt.StudentId) { receipt.MoveToStudent(owner); touched.Add(owner); }
        }
        foreach (var discount in Discounts)
        {
            var owner = Resolve(discount.StudentId);
            if (owner != discount.StudentId) discount.MoveToStudent(owner);
        }
        foreach (var studentId in touched.Concat(Receipts.Where(x => x.Unallocated > 0).Select(x => x.StudentId)).Distinct())
            SpreadCredit(studentId, now);
    }

    private void SyncEnrollment(TuitionEnrollmentRow enrollment, TuitionSessionRow[] sessions, DateOnly anchor,
        IReadOnlyList<ClassFee> history, DateOnly today, DateTimeOffset now, HashSet<Guid> touched)
    {
        var studentId = Resolve(enrollment.StudentId);
        var own = sessions.Where(x => x.StartUtc >= enrollment.EnrolledAtUtc).ToArray();
        if (own.Length == 0) return;
        // Left before their first session: never billed.
        if (enrollment.WithdrawnAtUtc is { } left && own[0].StartUtc >= left) return;
        // Billing starts the day the student joins (or the class starts), so a mid-cycle join is billed at once.
        var joinedOn = FinanceTime.LocalDate(enrollment.EnrolledAtUtc);
        var startDay = joinedOn > anchor ? joinedOn : anchor;
        if (startDay > today) return;
        var lastDay = enrollment.WithdrawnAtUtc is { } withdrawn && FinanceTime.LocalDate(withdrawn) < today
            ? FinanceTime.LocalDate(withdrawn) : today;
        var firstCycle = TuitionMath.CycleIndexOf(anchor, startDay);
        var lastCycle = TuitionMath.CycleIndexOf(anchor, lastDay);
        for (var cycle = Math.Max(0, firstCycle); cycle <= lastCycle; cycle++)
        {
            if (Charges.Any(x => x.EnrollmentId == enrollment.Id && x.CycleIndex == cycle)) continue;
            var start = TuitionMath.CycleStart(anchor, cycle);
            var end = TuitionMath.CycleEnd(anchor, cycle);
            var window = InWindow(sessions, start, end);
            if (window.Length == 0) continue;            // H6: nothing taught in this cycle
            var part = window.Count(x => x.StartUtc >= enrollment.EnrolledAtUtc);
            if (part == 0) continue;
            var fee = ClassFee.ForCycle(history, start, end);
            if (fee is null) continue;                   // no fee yet for this cycle
            var joined = part < window.Length;
            var dueBase = joined && joinedOn > start ? joinedOn : start;
            var charge = TuitionCharge.Create(CenterId, enrollment.ClassId, studentId, enrollment.Id, cycle, start, end,
                fee.MonthlyFee, joined ? part : null, joined ? window.Length : null,
                Discounts.Where(x => x.StudentId == studentId && x.ClassId == enrollment.ClassId),
                dueBase.AddDays(Settings.DueDays), now);
            Charges.Add(charge);
            _newCharges.Add(charge);
            touched.Add(studentId);
        }

        if (enrollment.WithdrawnAtUtc is not { } leftAt) return;
        var leftOn = FinanceTime.LocalDate(leftAt);
        var current = Charges.FirstOrDefault(x => x.EnrollmentId == enrollment.Id && x.EndedOn is null &&
            x.PeriodStart <= leftOn && x.PeriodEnd >= leftOn);
        if (current is null) return;
        var mine = InWindow(sessions, current.PeriodStart, current.PeriodEnd)
            .Where(x => x.StartUtc >= enrollment.EnrolledAtUtc).ToArray();
        var attended = mine.Count(x => x.StartUtc < leftAt);
        if (attended >= mine.Length) return;             // the whole cycle was taught: the month is due in full
        current.EndEarly(leftOn, attended, mine.Length, PaidFor(current.Id));
        touched.Add(studentId);
    }

    public static TuitionSessionRow[] InWindow(IEnumerable<TuitionSessionRow> sessions, DateOnly start, DateOnly end) =>
        sessions.Where(x =>
        {
            var day = FinanceTime.LocalDate(x.StartUtc);
            return day >= start && day <= end;
        }).ToArray();

    /// <summary>The cycle running today for a class, or null before its first session.</summary>
    public (DateOnly Start, DateOnly End, int Index)? CurrentCycle(Guid classId, DateOnly today)
    {
        DateOnly anchor;
        if (Anchors.TryGetValue(classId, out var fixedAnchor)) anchor = fixedAnchor;
        else
        {
            var first = SessionsOf(classId).FirstOrDefault();
            if (first is null) return null;
            anchor = FinanceTime.LocalDate(first.StartUtc);
        }
        var index = TuitionMath.CycleIndexOf(anchor, today);
        if (index < 0) return null;
        return (TuitionMath.CycleStart(anchor, index), TuitionMath.CycleEnd(anchor, index), index);
    }

    public DateOnly? FirstSessionDate(Guid classId) => Anchors.TryGetValue(classId, out var anchor) ? anchor
        : SessionsOf(classId).Select(x => (DateOnly?)FinanceTime.LocalDate(x.StartUtc)).FirstOrDefault();
}
