using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>
/// Center side of tuition (docs/HOC-PHI.md): class fees, charges, receipts, discounts and settings. Every call takes
/// the center guard, brings billing up to date, then reads or changes.
/// </summary>
public sealed class TuitionWorkflow(ITuitionStore store, ITuitionAcademicsReader reader, IUserDirectory users, IClock clock)
{
    // ---------------- queries ----------------

    public async Task<IReadOnlyList<ClassFeeRowDto>> ListClassFeesAsync(Guid centerId, CancellationToken token)
    {
        var (ledger, today) = await SyncedAsync(centerId, token);
        var names = await users.GetDisplayNamesAsync(ledger.Fees.Select(x => x.CreatedByUserId).Distinct(), token);
        return ledger.Classes.Values
            .Where(x => !x.IsArchived || ledger.Fees.Any(f => f.ClassId == x.Id))
            .Select(x => ClassRow(ledger, x, today, names))
            .OrderBy(x => x.IsArchived).ThenBy(x => x.ClassCode, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<TuitionPage<ChargeDto>> ListChargesAsync(Guid centerId, string? status, Guid? classId,
        Guid? studentId, string? search, int page, int pageSize, CancellationToken token)
    {
        var (ledger, today) = await SyncedAsync(centerId, token);
        var term = search?.Trim();
        var adjusters = await users.GetDisplayNamesAsync(ledger.Charges.Where(x => x.AdjustedByUserId.HasValue)
            .Select(x => x.AdjustedByUserId!.Value).Distinct(), token);
        var items = ledger.Charges
            .Where(x => classId is null || x.ClassId == classId)
            .Where(x => studentId is null || x.StudentId == studentId)
            .Select(x => TuitionViews.Charge(ledger, x, today, adjusters))
            .Where(x => status switch
            {
                "Open" => x.Remaining > 0,
                "Missed" => x.Missed.Count > 0,
                "Overdue" => x.Status == "Overdue",
                "Paid" => x.Status == "Paid",
                _ => true
            })
            .Where(x => string.IsNullOrEmpty(term) || x.StudentName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                x.StudentCode.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                x.ClassName.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(x => x.Status == "Paid").ThenBy(x => x.Status != "Overdue")
            .ThenByDescending(x => x.PeriodStart).ThenBy(x => x.StudentName, StringComparer.CurrentCulture)
            .ToArray();
        return new TuitionPage<ChargeDto>
        {
            Page = page, PageSize = pageSize, TotalCount = items.Length,
            Items = items.Skip((page - 1) * pageSize).Take(pageSize).ToList()
        };
    }

    public async Task<TuitionSummaryDto> SummaryAsync(Guid centerId, CancellationToken token)
    {
        var (ledger, today) = await SyncedAsync(centerId, token);
        var charges = ledger.Charges.Select(x => TuitionViews.Charge(ledger, x, today)).ToArray();
        var billedClasses = ledger.Fees.Select(x => x.ClassId).ToHashSet();
        return new TuitionSummaryDto
        {
            Owed = charges.Sum(x => x.Remaining),
            Overdue = charges.Where(x => x.Status == "Overdue").Sum(x => x.Remaining),
            OverdueStudents = charges.Where(x => x.Status == "Overdue").Select(x => x.StudentId).Distinct().Count(),
            UnpaidCharges = charges.Count(x => x.Remaining > 0),
            CollectedThisMonth = ledger.Receipts.Where(x => !x.IsVoided && x.ReceivedOn.Year == today.Year &&
                x.ReceivedOn.Month == today.Month).Sum(x => x.Amount),
            Credit = ledger.Receipts.Sum(x => x.Unallocated),
            ClassesWithoutFee = ledger.Classes.Values.Count(x => !x.IsArchived && !billedClasses.Contains(x.Id) &&
                ledger.Enrollments.Any(e => e.ClassId == x.Id && e.WithdrawnAtUtc is null)),
            Today = today
        };
    }

    public async Task<Result<StudentTuitionDto>> StudentAccountAsync(Guid centerId, Guid studentId, CancellationToken token)
    {
        var (ledger, today) = await SyncedAsync(centerId, token);
        var student = ledger.FindStudent(studentId);
        if (student is null) return Result<StudentTuitionDto>.Failure(TuitionErrors.NotFound);
        var names = await users.GetDisplayNamesAsync(TuitionViews.Actors(ledger), token);
        var centerName = (await reader.ReadCenterNamesAsync([centerId], token)).GetValueOrDefault(centerId) ?? string.Empty;
        return Result<StudentTuitionDto>.Success(TuitionViews.Student(ledger, student, centerName, today, names));
    }

    public async Task<TuitionPage<ReceiptDto>> ListReceiptsAsync(Guid centerId, DateOnly? from, DateOnly? to,
        string? method, string? search, bool includeVoided, int page, int pageSize, CancellationToken token)
    {
        var (ledger, _) = await SyncedAsync(centerId, token);
        var term = search?.Trim();
        var filtered = ledger.Receipts
            .Where(x => includeVoided || !x.IsVoided)
            .Where(x => from is null || x.ReceivedOn >= from)
            .Where(x => to is null || x.ReceivedOn <= to)
            .Where(x => method is null || x.Method.ToString() == method)
            .Where(x =>
            {
                if (string.IsNullOrEmpty(term)) return true;
                var student = ledger.FindStudent(x.StudentId);
                return Domain.Tuition.Receipt.FormatNumber(x.Number).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (student?.FullName.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
                    (student?.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (x.PayerName?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false);
            })
            .OrderByDescending(x => x.Number).ToArray();
        var pageItems = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var names = await users.GetDisplayNamesAsync(pageItems.SelectMany(x =>
            x.VoidedByUserId is { } v ? [x.CreatedByUserId, v] : new[] { x.CreatedByUserId }).Distinct(), token);
        return new TuitionPage<ReceiptDto>
        {
            Page = page, PageSize = pageSize, TotalCount = filtered.Length,
            Items = pageItems.Select(x => TuitionViews.Receipt(ledger, x, names)).ToList()
        };
    }

    public async Task<Result<ReceiptDto>> GetReceiptAsync(Guid centerId, Guid receiptId, CancellationToken token)
    {
        var (ledger, _) = await SyncedAsync(centerId, token);
        var receipt = ledger.Receipts.FirstOrDefault(x => x.Id == receiptId);
        if (receipt is null) return Result<ReceiptDto>.Failure(TuitionErrors.NotFound);
        var names = await users.GetDisplayNamesAsync(TuitionViews.Actors(ledger), token);
        return Result<ReceiptDto>.Success(TuitionViews.Receipt(ledger, receipt, names));
    }

    public async Task<FinanceSettingsDto> GetSettingsAsync(Guid centerId, CancellationToken token) =>
        Settings(await store.FindSettingsAsync(centerId, token) ?? FinanceSettings.Default(centerId));

    // ---------------- commands ----------------

    /// <summary>H3: a new fee applies from the next cycle; the first fee of a class also covers the running cycle.</summary>
    public async Task<Result<ClassFeeRowDto>> SetFeeAsync(Guid centerId, Guid classId, long monthlyFee, Guid actorId,
        CancellationToken token)
    {
        if (!TuitionMath.IsValidFee(monthlyFee)) return Result<ClassFeeRowDto>.Failure(TuitionErrors.InvalidFee);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        if (!ledger.Classes.TryGetValue(classId, out var cls)) return Result<ClassFeeRowDto>.Failure(TuitionErrors.NotFound);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        var latest = ledger.FeeHistory(classId).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        if (latest is null || latest.MonthlyFee != monthlyFee)
        {
            var fee = ClassFee.Create(centerId, classId, monthlyFee, today, actorId, now);
            ledger.Fees.Add(fee);
            await store.AddAsync(fee, token);
        }
        ledger.Sync(today, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        var names = await users.GetDisplayNamesAsync(ledger.Fees.Select(x => x.CreatedByUserId).Distinct(), token);
        return Result<ClassFeeRowDto>.Success(ClassRow(ledger, cls, today, names));
    }

    public async Task<Result<ReceiptDto>> RecordPaymentAsync(Guid centerId, Guid studentId, long amount, string? method,
        DateOnly? receivedOn, string? payerName, string? note, IReadOnlyList<Guid>? chargeIds, Guid? requestId,
        Guid actorId, CancellationToken token)
    {
        if (amount <= 0 || amount > 10 * TuitionMath.MaxFee || !Enum.TryParse<PaymentMethod>(method, false, out var how) ||
            !Enum.IsDefined(how) || !Receipt.IsValidText(payerName, Receipt.MaxPayerLength) ||
            !Receipt.IsValidText(note, Receipt.MaxNoteLength))
            return Result<ReceiptDto>.Failure(TuitionErrors.InvalidPayment);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        var day = receivedOn ?? today;
        if (day > today || day < today.AddDays(-366)) return Result<ReceiptDto>.Failure(TuitionErrors.InvalidReceivedOn);

        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        ledger.Sync(today, now);
        if (requestId is { } request && ledger.Receipts.FirstOrDefault(x => x.RequestId == request) is { } replay)
        {
            if (replay.StudentId != ledger.Resolve(studentId) || replay.Amount != amount)
                return Result<ReceiptDto>.Failure(TuitionErrors.ConcurrentChange);
            await PersistAsync(ledger, token);
            await guard.CommitAsync(token);
            return Result<ReceiptDto>.Success(await ReceiptViewAsync(ledger, replay, token));
        }
        var student = ledger.FindStudent(studentId);
        if (student is null) return Result<ReceiptDto>.Failure(TuitionErrors.NotFound);
        if (student.MergedIntoStudentId.HasValue) return Result<ReceiptDto>.Failure(TuitionErrors.StudentUnavailable);
        var first = chargeIds?.Distinct().ToArray() ?? [];
        var open = ledger.OpenCharges(studentId).Select(x => x.Id).ToHashSet();
        if (first.Any(id => !open.Contains(id))) return Result<ReceiptDto>.Failure(TuitionErrors.InvalidCharges);

        var number = await store.NextReceiptNumberAsync(centerId, token);
        var receipt = Receipt.Create(centerId, number, studentId, payerName, how, amount, day, note, requestId, actorId, now);
        ledger.Receipts.Add(receipt);
        await store.AddAsync(receipt, token);
        ledger.Spread(receipt, first, now);
        try { await PersistAsync(ledger, token); }
        catch (TuitionPersistenceException) { return Result<ReceiptDto>.Failure(TuitionErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<ReceiptDto>.Success(await ReceiptViewAsync(ledger, receipt, token));
    }

    /// <summary>H12: the receipt stays, its money stops counting; other credit of the student is spread again.</summary>
    public async Task<Result<ReceiptDto>> VoidReceiptAsync(Guid centerId, Guid receiptId, string? reason, Guid actorId,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > Receipt.MaxNoteLength)
            return Result<ReceiptDto>.Failure(TuitionErrors.InvalidReason);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        ledger.Sync(FinanceTime.LocalDate(now), now);
        var receipt = ledger.Receipts.FirstOrDefault(x => x.Id == receiptId);
        if (receipt is null) return Result<ReceiptDto>.Failure(TuitionErrors.NotFound);
        if (receipt.IsVoided) return Result<ReceiptDto>.Failure(TuitionErrors.AlreadyVoided);
        receipt.Void(reason, actorId, now);
        ledger.SpreadCredit(receipt.StudentId, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        return Result<ReceiptDto>.Success(await ReceiptViewAsync(ledger, receipt, token));
    }

    /// <summary>
    /// H18: the center's own decision to reduce one cycle for missed sessions. If the cycle was already paid, the
    /// reduction becomes credit and pays the next cycle.
    /// </summary>
    public async Task<Result<ChargeDto>> AdjustChargeAsync(Guid centerId, Guid chargeId, long amount, int? sessions,
        string? reason, Guid actorId, CancellationToken token)
    {
        if (amount < 0 || amount % 1000 != 0 || sessions is < 0 or > 100 ||
            (amount > 0 && (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > TuitionCharge.MaxReasonLength)))
            return Result<ChargeDto>.Failure(TuitionErrors.InvalidAdjustment);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        ledger.Sync(today, now);
        var charge = ledger.Charges.FirstOrDefault(x => x.Id == chargeId);
        if (charge is null) return Result<ChargeDto>.Failure(TuitionErrors.NotFound);
        if (charge.EndedOn is not null) return Result<ChargeDto>.Failure(TuitionErrors.ChargeEnded);
        if (amount > charge.ListAmount - charge.DiscountAmount) return Result<ChargeDto>.Failure(TuitionErrors.InvalidAdjustment);
        charge.Adjust(amount, sessions, reason, actorId, now);
        ledger.ReleaseExcess(charge);
        ledger.SpreadCredit(charge.StudentId, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        var names = await users.GetDisplayNamesAsync([actorId], token);
        return Result<ChargeDto>.Success(TuitionViews.Charge(ledger, charge, today, names));
    }

    /// <summary>H8: also reprices the student's cycles of that class already billed from <paramref name="fromDate"/>.</summary>
    public async Task<Result<StudentTuitionDto>> AddDiscountAsync(Guid centerId, Guid studentId, Guid classId,
        string? kind, long value, DateOnly fromDate, DateOnly? toDate, string? reason, Guid actorId, CancellationToken token)
    {
        if (!Enum.TryParse<DiscountKind>(kind, false, out var how) || !Enum.IsDefined(how) ||
            !TuitionDiscount.IsValid(how, value, fromDate, toDate, reason))
            return Result<StudentTuitionDto>.Failure(TuitionErrors.InvalidDiscount);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        ledger.Sync(today, now);
        var student = ledger.FindStudent(studentId);
        if (student is null || !ledger.Classes.ContainsKey(classId)) return Result<StudentTuitionDto>.Failure(TuitionErrors.NotFound);
        if (student.MergedIntoStudentId.HasValue) return Result<StudentTuitionDto>.Failure(TuitionErrors.StudentUnavailable);
        if (!ledger.Enrollments.Any(x => x.ClassId == classId && ledger.Resolve(x.StudentId) == studentId))
            return Result<StudentTuitionDto>.Failure(TuitionErrors.NotEnrolled);

        var discount = TuitionDiscount.Create(centerId, studentId, classId, how, value, fromDate, toDate, reason!, actorId, now);
        ledger.Discounts.Add(discount);
        await store.AddAsync(discount, token);
        var mine = ledger.Discounts.Where(x => x.StudentId == studentId && x.ClassId == classId).ToArray();
        foreach (var charge in ledger.Charges.Where(x => x.StudentId == studentId && x.ClassId == classId &&
                     discount.AppliesTo(x.PeriodStart)))
            if (charge.Reprice(mine)) ledger.ReleaseExcess(charge);
        ledger.SpreadCredit(studentId, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        return await StudentViewAsync(ledger, student, today, token);
    }

    /// <summary>Stops a discount for cycles billed from now on; cycles already billed keep their amount.</summary>
    public async Task<Result<StudentTuitionDto>> RevokeDiscountAsync(Guid centerId, Guid discountId, Guid actorId,
        CancellationToken token)
    {
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        ledger.Sync(today, now);
        var discount = ledger.Discounts.FirstOrDefault(x => x.Id == discountId);
        if (discount is null) return Result<StudentTuitionDto>.Failure(TuitionErrors.NotFound);
        discount.Revoke(actorId, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        var student = ledger.FindStudent(discount.StudentId);
        return student is null ? Result<StudentTuitionDto>.Failure(TuitionErrors.NotFound)
            : await StudentViewAsync(ledger, student, today, token);
    }

    public async Task<Result<FinanceSettingsDto>> UpdateSettingsAsync(Guid centerId, string? bankBin, string? accountNumber,
        string? accountName, int dueDays, bool emailReminders, int expectedVersion, Guid actorId, CancellationToken token)
    {
        if (!FinanceSettings.IsValid(bankBin, accountNumber, accountName, dueDays))
            return Result<FinanceSettingsDto>.Failure(TuitionErrors.InvalidSettings);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var settings = await store.FindSettingsAsync(centerId, token);
        if ((settings?.Version ?? 0) != expectedVersion) return Result<FinanceSettingsDto>.Failure(TuitionErrors.SettingsVersion);
        if (settings is null)
        {
            settings = FinanceSettings.Default(centerId);
            await store.AddAsync(settings, token);
        }
        settings.Update(bankBin, accountNumber, accountName, dueDays, emailReminders, actorId, clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TuitionPersistenceException) { return Result<FinanceSettingsDto>.Failure(TuitionErrors.SettingsVersion); }
        await guard.CommitAsync(token);
        return Result<FinanceSettingsDto>.Success(Settings(settings));
    }

    // ---------------- helpers ----------------

    private async Task<(CenterLedger Ledger, DateOnly Today)> SyncedAsync(Guid centerId, CancellationToken token)
    {
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        var today = FinanceTime.LocalDate(now);
        ledger.Sync(today, now);
        await PersistAsync(ledger, token);
        await guard.CommitAsync(token);
        return (ledger, today);
    }

    internal static async Task PersistAsync(CenterLedger ledger, ITuitionStore store, CancellationToken token)
    {
        foreach (var anchor in ledger.NewAnchors) await store.AddAsync(anchor, token);
        foreach (var charge in ledger.NewCharges) await store.AddAsync(charge, token);
        await store.SaveAsync(token);
    }

    private Task PersistAsync(CenterLedger ledger, CancellationToken token) => PersistAsync(ledger, store, token);

    private async Task<ReceiptDto> ReceiptViewAsync(CenterLedger ledger, Receipt receipt, CancellationToken token)
    {
        var names = await users.GetDisplayNamesAsync(
            receipt.VoidedByUserId is { } v ? [receipt.CreatedByUserId, v] : [receipt.CreatedByUserId], token);
        return TuitionViews.Receipt(ledger, receipt, names);
    }

    private async Task<Result<StudentTuitionDto>> StudentViewAsync(CenterLedger ledger, TuitionStudentRow student,
        DateOnly today, CancellationToken token)
    {
        var names = await users.GetDisplayNamesAsync(TuitionViews.Actors(ledger), token);
        var centerName = (await reader.ReadCenterNamesAsync([ledger.CenterId], token)).GetValueOrDefault(ledger.CenterId) ?? string.Empty;
        return Result<StudentTuitionDto>.Success(TuitionViews.Student(ledger, student, centerName, today, names));
    }

    private static ClassFeeRowDto ClassRow(CenterLedger ledger, TuitionClassRow cls, DateOnly today,
        IReadOnlyDictionary<Guid, string> names)
    {
        var history = ledger.FeeHistory(cls.Id).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.CreatedAtUtc).ToArray();
        var cycle = ledger.CurrentCycle(cls.Id, today);
        var latest = history.FirstOrDefault();
        var current = cycle is { } c ? ClassFee.ForCycle(history, c.Start, c.End) : latest;
        var next = cycle is { } running && latest is not null && latest.EffectiveFrom > running.Start && latest != current
            ? latest : null;
        var billed = cycle is { } now ? ledger.Charges.Where(x => x.ClassId == cls.Id && x.PeriodStart == now.Start).ToArray() : [];
        return new ClassFeeRowDto
        {
            ClassId = cls.Id, ClassCode = cls.Code, ClassName = cls.Name, IsArchived = cls.IsArchived,
            CurrentFee = current?.MonthlyFee, NextFee = next?.MonthlyFee, NextFeeFrom = next is null ? null : cycle?.End.AddDays(1),
            FirstSessionDate = ledger.FirstSessionDate(cls.Id), CurrentPeriodStart = cycle?.Start, CurrentPeriodEnd = cycle?.End,
            ActiveStudents = ledger.Enrollments.Count(x => x.ClassId == cls.Id && x.WithdrawnAtUtc is null),
            CurrentCycleBilled = billed.Sum(x => x.Amount), CurrentCyclePaid = billed.Sum(x => Math.Min(x.Amount, ledger.PaidFor(x.Id))),
            History = history.Select(x => new ClassFeeHistoryDto
            {
                MonthlyFee = x.MonthlyFee, EffectiveFrom = x.EffectiveFrom, CreatedAtUtc = x.CreatedAtUtc,
                CreatedByName = names.GetValueOrDefault(x.CreatedByUserId)
            }).ToList()
        };
    }

    private static FinanceSettingsDto Settings(FinanceSettings x) => new()
    {
        BankBin = x.BankBin, BankAccountNumber = x.BankAccountNumber, BankAccountName = x.BankAccountName,
        DueDays = x.DueDays, EmailReminders = x.EmailReminders, Version = x.Version
    };
}
