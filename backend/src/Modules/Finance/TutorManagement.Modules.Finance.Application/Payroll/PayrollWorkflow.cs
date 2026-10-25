using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payroll;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payroll;

public enum PayrollRole
{
    Owner,
    Manager,
    Teacher,
    Staff
}

public sealed record PayrollActor(Guid UserId, PayrollRole Role)
{
    public bool SeesAll => Role is PayrollRole.Owner or PayrollRole.Manager;
    public bool IsOwner => Role == PayrollRole.Owner;
}

/// <summary>A row was written twice (unique index) or changed concurrently.</summary>
public sealed class PayrollPersistenceException(string? constraintName, Exception inner)
    : Exception("Payroll data changed concurrently.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}

/// <summary>
/// docs/LUONG-VA-CHIA-THU-NHAP.md. Every call takes the center guard (shared with tuition), brings tuition up to date
/// when a month shares revenue, loads the center's payroll book, then reads or changes it.
/// </summary>
public sealed class PayrollWorkflow(
    IPayrollStore store,
    IPayrollAcademicsReader reader,
    ITuitionStore tuition,
    ITuitionAcademicsReader tuitionReader,
    IUserDirectory users,
    IClock clock)
{
    // ---------------- platform admin (A1, A2) ----------------

    public async Task<Result<PayrollConfigDto>> GetConfigAsync(Guid centerId, CancellationToken token)
    {
        if (!await reader.CenterExistsAsync(centerId, token)) return Result<PayrollConfigDto>.Failure(PayrollErrors.NotFound);
        return Result<PayrollConfigDto>.Success(await ConfigDtoAsync(centerId, token));
    }

    public async Task<Result<PayrollConfigDto>> SetConfigAsync(Guid centerId, bool enabled, string? modeText,
        string? effectiveMonth, string? reason, int expectedVersion, Guid actorId, CancellationToken token)
    {
        if (!await reader.CenterExistsAsync(centerId, token)) return Result<PayrollConfigDto>.Failure(PayrollErrors.NotFound);
        var current = PayMonth.Of(FinanceTime.LocalDate(clock.UtcNow));
        if (!Enum.TryParse<PayrollMode>(modeText, out var mode) || !Enum.IsDefined(mode) ||
            !PayMonth.TryParse(effectiveMonth, out var effective) || (effective != current && effective != current.AddMonths(1)) ||
            !PayrollText.IsReason(reason))
            return Result<PayrollConfigDto>.Failure(PayrollErrors.InvalidConfig);

        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var config = await store.FindConfigAsync(centerId, token);
        if ((config?.Version ?? 0) != expectedVersion) return Result<PayrollConfigDto>.Failure(PayrollErrors.ConfigVersion);
        if ((await store.ListClosedMonthsAsync(centerId, token)).Any(x => x.Month >= effective))
            return Result<PayrollConfigDto>.Failure(PayrollErrors.ConfigMonthClosed);
        if (config is null)
        {
            config = PayrollConfig.Create(centerId);
            await store.AddAsync(config, token);
        }

        await store.AddAsync(config.Change(enabled, mode, effective, reason!, actorId, clock.UtcNow), token);
        if (!await SaveAsync(token)) return Result<PayrollConfigDto>.Failure(PayrollErrors.ConfigVersion);
        await guard.CommitAsync(token);
        return Result<PayrollConfigDto>.Success(await ConfigDtoAsync(centerId, token));
    }

    /// <summary>A1: every operating center with its payroll switch, for the platform overview.</summary>
    public async Task<PayrollConfigOverviewDto> ConfigOverviewAsync(CancellationToken token)
    {
        var centers = await reader.ListOperatingCentersAsync(token);
        var changes = (await store.ListAllConfigChangesAsync(token)).GroupBy(x => x.CenterId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAtUtc).ToArray());
        var closed = await store.LatestClosedMonthsAsync(token);
        var current = PayMonth.Of(FinanceTime.LocalDate(clock.UtcNow));
        var names = await users.GetDisplayNamesAsync(changes.Values.Select(x => x[^1].ActorUserId).Distinct(), token);
        return new PayrollConfigOverviewDto
        {
            CurrentMonth = PayMonth.Format(current),
            Centers = centers.Select(center =>
            {
                var own = changes.GetValueOrDefault(center.Id) ?? [];
                var now = own.LastOrDefault(x => x.EffectiveFrom <= current);
                var last = own.Length == 0 ? null : own[^1];
                var scheduled = last is not null && last.EffectiveFrom > current ? last : null;
                return new PayrollCenterConfigSummaryDto
                {
                    CenterId = center.Id,
                    CenterName = center.DisplayName,
                    CenterStatus = center.Status,
                    EnabledThisMonth = now?.Enabled ?? false,
                    ModeThisMonth = (now?.Mode ?? PayrollMode.RevenueShare).ToString(),
                    NextMonth = scheduled is null ? null : PayMonth.Format(scheduled.EffectiveFrom),
                    NextEnabled = scheduled?.Enabled,
                    NextMode = scheduled?.Mode.ToString(),
                    LatestClosedMonth = closed.TryGetValue(center.Id, out var month) ? PayMonth.Format(month) : null,
                    LastChangedAtUtc = last?.CreatedAtUtc,
                    LastChangedBy = last is null ? null : names.GetValueOrDefault(last.ActorUserId)
                };
            }).ToList()
        };
    }

    private async Task<PayrollConfigDto> ConfigDtoAsync(Guid centerId, CancellationToken token)
    {
        var config = await store.FindConfigAsync(centerId, token);
        var changes = await store.ListConfigChangesAsync(centerId, token);
        var closed = await store.ListClosedMonthsAsync(centerId, token);
        var current = PayMonth.Of(FinanceTime.LocalDate(clock.UtcNow));
        var now = changes.OrderBy(x => x.CreatedAtUtc).LastOrDefault(x => x.EffectiveFrom <= current);
        var names = await users.GetDisplayNamesAsync(changes.Select(x => x.ActorUserId).Distinct(), token);
        return new PayrollConfigDto
        {
            CenterId = centerId,
            Enabled = config?.Enabled ?? false,
            Mode = (config?.Mode ?? PayrollMode.RevenueShare).ToString(),
            EffectiveMonth = config is null ? null : PayMonth.Format(config.EffectiveFrom),
            EnabledThisMonth = now?.Enabled ?? false,
            ModeThisMonth = (now?.Mode ?? PayrollMode.RevenueShare).ToString(),
            CurrentMonth = PayMonth.Format(current),
            LatestClosedMonth = closed.Count == 0 ? null : PayMonth.Format(closed.Max(x => x.Month)),
            Version = config?.Version ?? 0,
            History = changes.OrderByDescending(x => x.CreatedAtUtc).Select(x => new PayrollConfigChangeDto
            {
                Id = x.Id, PreviousEnabled = x.PreviousEnabled, PreviousMode = x.PreviousMode.ToString(), Enabled = x.Enabled,
                Mode = x.Mode.ToString(), EffectiveMonth = PayMonth.Format(x.EffectiveFrom), Reason = x.Reason,
                ActorName = names.GetValueOrDefault(x.ActorUserId), CreatedAtUtc = x.CreatedAtUtc
            }).ToList()
        };
    }

    // ---------------- center: reads ----------------

    public async Task<PayrollStatusDto> StatusAsync(Guid centerId, PayrollActor actor, CancellationToken token)
    {
        var changes = (await store.ListConfigChangesAsync(centerId, token)).OrderBy(x => x.CreatedAtUtc).ToArray();
        var current = PayMonth.Of(FinanceTime.LocalDate(clock.UtcNow));
        var setting = changes.LastOrDefault(x => x.EffectiveFrom <= current);
        var mine = (await reader.ListTeachersAsync(centerId, token)).FirstOrDefault(x => x.UserId == actor.UserId);
        return new PayrollStatusDto
        {
            Enabled = setting?.Enabled ?? false,
            Mode = (setting?.Mode ?? PayrollMode.RevenueShare).ToString(),
            CurrentMonth = PayMonth.Format(current),
            MyTeacherId = mine?.Id,
            CanSeeAll = actor.SeesAll,
            CanEdit = actor.SeesAll,
            CanCloseAndPay = actor.IsOwner
        };
    }

    public async Task<Result<List<PayrollMonthSummaryDto>>> ListMonthsAsync(Guid centerId, PayrollActor actor,
        CancellationToken token)
    {
        if (!actor.SeesAll) return Result<List<PayrollMonthSummaryDto>>.Failure(PayrollErrors.Forbidden);
        var book = await ReadAsync(centerId, token);
        if (!book.IsEnabledNow) return Result<List<PayrollMonthSummaryDto>>.Failure(PayrollErrors.Disabled);
        return Result<List<PayrollMonthSummaryDto>>.Success(book.MonthsInScope().Select(month =>
        {
            var lines = book.LinesOf(month);
            var figures = book.Figures(month);
            return new PayrollMonthSummaryDto
            {
                Month = PayMonth.Format(month),
                Mode = book.ModeOf(month).ToString(),
                Status = book.IsClosed(month) ? "Closed" : "Draft",
                Teachers = lines.Count,
                Sessions = lines.Sum(x => x.Sessions),
                Net = lines.Sum(x => x.Net),
                Paid = lines.Sum(x => x.Paid),
                Warnings = book.IsClosed(month) ? 0 : figures.Unsubmitted.Count + figures.MissingRates.Count + figures.Unassigned.Count,
                OpenDisputes = book.Disputes.Count(x => x.Month == month && x.IsOpen)
            };
        }).ToList());
    }

    public async Task<Result<PayrollMonthDto>> GetMonthAsync(Guid centerId, string monthText, PayrollActor actor,
        CancellationToken token)
    {
        if (actor.Role == PayrollRole.Staff) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        var book = await ReadAsync(centerId, token);
        if (!book.IsEnabledNow) return Result<PayrollMonthDto>.Failure(PayrollErrors.Disabled);
        if (!InScope(book, monthText, out var month)) return Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidMonth);
        Guid? only = null;
        if (!actor.SeesAll)
        {
            only = book.Teachers.Values.FirstOrDefault(x => x.UserId == actor.UserId)?.Id;
            if (only is null) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        }

        return Result<PayrollMonthDto>.Success(await MonthDtoAsync(book, month, only, token));
    }

    public async Task<Result<List<PayRateDto>>> ListRatesAsync(Guid centerId, PayrollActor actor, CancellationToken token)
    {
        if (!actor.SeesAll) return Result<List<PayRateDto>>.Failure(PayrollErrors.Forbidden);
        var rates = await store.ListRatesAsync(centerId, token);
        var teachers = (await reader.ListTeachersAsync(centerId, token)).ToDictionary(x => x.Id);
        var classes = (await reader.ListClassesAsync(centerId, token)).ToDictionary(x => x.Id);
        var names = await users.GetDisplayNamesAsync(rates.Select(x => x.CreatedByUserId).Distinct(), token);
        return Result<List<PayRateDto>>.Success(rates
            .OrderBy(x => x.IsVoided).ThenBy(x => teachers.GetValueOrDefault(x.TeacherId)?.FullName, StringComparer.CurrentCulture)
            .ThenBy(x => x.ClassId.HasValue).ThenByDescending(x => x.EffectiveFrom)
            .Select(x => RateDto(x, teachers, classes, names)).ToList());
    }

    /// <summary>C15: the caller's money in every center that has payroll on.</summary>
    public async Task<MyEarningsDto> MyEarningsAsync(Guid userId, int year, CancellationToken token)
    {
        var profiles = await reader.ListTeacherProfilesOfUserAsync(userId, token);
        var centers = new List<MyEarningsCenterDto>();
        var centerNames = await reader.ReadCenterNamesAsync(profiles.Select(x => x.CenterId).Distinct().ToArray(), token);
        foreach (var group in profiles.GroupBy(x => x.CenterId))
        {
            var book = await ReadAsync(group.Key, token);
            if (!book.Changes.Any(x => x.Enabled)) continue;
            var teacherIds = group.Select(x => x.TeacherId).ToHashSet();
            var months = book.MonthsInScope().Where(x => x.Year == year).Select(month =>
            {
                var lines = book.LinesOf(month).Where(x => teacherIds.Contains(x.TeacherId)).ToArray();
                return new MyEarningsMonthDto
                {
                    Month = PayMonth.Format(month),
                    Status = book.IsClosed(month) ? "Closed" : "Draft",
                    Sessions = lines.Sum(x => x.Sessions), Minutes = lines.Sum(x => x.Minutes),
                    Net = lines.Sum(x => x.Net), Paid = lines.Sum(x => x.Paid), Remaining = lines.Sum(x => x.Remaining)
                };
            }).Where(x => x.Sessions > 0 || x.Net != 0 || x.Paid != 0).ToList();
            if (months.Count == 0 && !book.IsEnabledNow) continue;
            centers.Add(new MyEarningsCenterDto
            {
                CenterId = group.Key,
                CenterName = centerNames.GetValueOrDefault(group.Key) ?? "—",
                Mode = book.ModeOf(book.CurrentMonth).ToString(),
                TeacherIds = [.. teacherIds],
                YearNet = months.Sum(x => x.Net), YearPaid = months.Sum(x => x.Paid),
                YearSessions = months.Sum(x => x.Sessions), YearMinutes = months.Sum(x => x.Minutes),
                Months = months
            });
        }

        var byMonth = centers.SelectMany(x => x.Months).GroupBy(x => x.Month).OrderByDescending(x => x.Key)
            .Select(x => new MyEarningsMonthDto
            {
                Month = x.Key,
                Status = x.All(m => m.Status == "Closed") ? "Closed" : "Draft",
                Sessions = x.Sum(m => m.Sessions), Minutes = x.Sum(m => m.Minutes),
                Net = x.Sum(m => m.Net), Paid = x.Sum(m => m.Paid), Remaining = x.Sum(m => m.Remaining)
            }).ToList();
        return new MyEarningsDto
        {
            Year = year,
            YearNet = byMonth.Sum(x => x.Net), YearPaid = byMonth.Sum(x => x.Paid),
            YearSessions = byMonth.Sum(x => x.Sessions), YearMinutes = byMonth.Sum(x => x.Minutes),
            Months = byMonth,
            Centers = centers.OrderBy(x => x.CenterName, StringComparer.CurrentCulture).ToList()
        };
    }

    // ---------------- center: month lifecycle (C8, C9) ----------------

    public async Task<Result<PayrollMonthDto>> CloseAsync(Guid centerId, string monthText, PayrollActor actor,
        CancellationToken token)
    {
        if (!actor.IsOwner) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        return await WriteAsync(centerId, monthText, async (book, month) =>
        {
            var blockers = book.CloseBlockers(month);
            if (blockers.Count > 0)
                return new Error(PayrollErrors.CannotClose.Code,
                    $"The month cannot be closed: {string.Join(", ", blockers)}.", "blockers");
            var figures = book.Figures(month);
            var lines = book.LinesOf(month)
                .Where(x => x.Sessions != 0 || x.Base != 0 || x.Adjustments != 0 || x.Carried != 0)
                .Select(x => new PayrollMonthLine(x.TeacherId, x.Sessions, x.Minutes, x.Base, x.Adjustments, x.Carried));
            foreach (var (from, teacherId, amount) in book.CarriedInto(month))
                await store.AddAsync(new PayrollCarry(centerId, from, month, teacherId, amount), token);
            await store.AddAsync(PayrollClosedMonth.Create(centerId, month, figures.Mode, actor.UserId, clock.UtcNow, lines), token);
            await store.AddAsync(new PayrollMonthEvent(centerId, month, PayrollMonthEventKind.Closed, null, actor.UserId,
                clock.UtcNow), token);
            return Error.None;
        }, actor, token);
    }

    public async Task<Result<PayrollMonthDto>> ReopenAsync(Guid centerId, string monthText, string? reason,
        PayrollActor actor, CancellationToken token)
    {
        if (!actor.IsOwner) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        if (!PayrollText.IsReason(reason)) return Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidReason);
        return await WriteAsync(centerId, monthText, async (book, month) =>
        {
            if (book.LatestClosed != month) return PayrollErrors.CannotReopen;
            store.Remove(book.Closed[month]);
            foreach (var carry in book.Carries.Where(x => x.IntoMonth == month)) store.Remove(carry);
            await store.AddAsync(new PayrollMonthEvent(centerId, month, PayrollMonthEventKind.Reopened, reason!.Trim(),
                actor.UserId, clock.UtcNow), token);
            return Error.None;
        }, actor, token, allowClosed: true);
    }

    // ---------------- center: entries ----------------

    public Task<Result<PayrollMonthDto>> AddAdjustmentAsync(Guid centerId, string monthText, Guid teacherId, long amount,
        string? reason, PayrollActor actor, CancellationToken token)
    {
        if (!actor.SeesAll) return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden));
        if (!PayrollAdjustment.IsValidAmount(amount) || !PayrollText.IsReason(reason))
            return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidAdjustment));
        return WriteAsync(centerId, monthText, async (book, month) =>
        {
            if (!book.Teachers.ContainsKey(teacherId)) return PayrollErrors.NotFound;
            await store.AddAsync(PayrollAdjustment.Create(centerId, month, teacherId, amount, reason!, actor.UserId,
                clock.UtcNow), token);
            return Error.None;
        }, actor, token);
    }

    public Task<Result<PayrollMonthDto>> VoidAdjustmentAsync(Guid centerId, Guid adjustmentId, string? reason,
        PayrollActor actor, CancellationToken token) =>
        VoidEntryAsync(centerId, reason, actor, actor.SeesAll, book => book.Adjustments.FirstOrDefault(x => x.Id == adjustmentId),
            entry => ((PayrollAdjustment)entry).Month, token);

    public Task<Result<PayrollMonthDto>> AddExpenseAsync(Guid centerId, string monthText, DateOnly spentOn, string? category,
        long amount, string? note, PayrollActor actor, CancellationToken token)
    {
        if (!actor.SeesAll) return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden));
        return WriteAsync(centerId, monthText, async (book, month) =>
        {
            if (book.ModeOf(month) != PayrollMode.RevenueShare) return PayrollErrors.WrongMode;
            if (!SharedExpense.IsValid(month, spentOn, category, amount, note)) return PayrollErrors.InvalidExpense;
            await store.AddAsync(SharedExpense.Create(centerId, month, spentOn, category!, amount, note!, actor.UserId,
                clock.UtcNow), token);
            return Error.None;
        }, actor, token);
    }

    public Task<Result<PayrollMonthDto>> VoidExpenseAsync(Guid centerId, Guid expenseId, string? reason, PayrollActor actor,
        CancellationToken token) =>
        VoidEntryAsync(centerId, reason, actor, actor.SeesAll, book => book.Expenses.FirstOrDefault(x => x.Id == expenseId),
            entry => ((SharedExpense)entry).Month, token);

    public async Task<Result<PayRateDto>> AddRateAsync(Guid centerId, Guid teacherId, Guid? classId, long amount,
        DateOnly effectiveFrom, PayrollActor actor, CancellationToken token)
    {
        if (!actor.SeesAll) return Result<PayRateDto>.Failure(PayrollErrors.Forbidden);
        if (!PayRate.IsValidAmount(amount) || classId == Guid.Empty) return Result<PayRateDto>.Failure(PayrollErrors.InvalidRate);
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var teachers = (await reader.ListTeachersAsync(centerId, token)).ToDictionary(x => x.Id);
        var classes = (await reader.ListClassesAsync(centerId, token)).ToDictionary(x => x.Id);
        if (!teachers.ContainsKey(teacherId) || (classId is { } id && !classes.ContainsKey(id)))
            return Result<PayRateDto>.Failure(PayrollErrors.NotFound);
        if (!await EnabledAsync(centerId, token)) return Result<PayRateDto>.Failure(PayrollErrors.Disabled);
        var rate = PayRate.Create(centerId, teacherId, classId, amount, effectiveFrom, actor.UserId, clock.UtcNow);
        await store.AddAsync(rate, token);
        if (!await SaveAsync(token)) return Result<PayRateDto>.Failure(PayrollErrors.ConcurrentChange);
        await guard.CommitAsync(token);
        var names = await users.GetDisplayNamesAsync([actor.UserId], token);
        return Result<PayRateDto>.Success(RateDto(rate, teachers, classes, names));
    }

    public async Task<Result<PayRateDto>> VoidRateAsync(Guid centerId, Guid rateId, string? reason, PayrollActor actor,
        CancellationToken token)
    {
        if (!actor.SeesAll) return Result<PayRateDto>.Failure(PayrollErrors.Forbidden);
        if (!PayrollText.IsReason(reason)) return Result<PayRateDto>.Failure(PayrollErrors.InvalidReason);
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var rate = (await store.ListRatesAsync(centerId, token)).FirstOrDefault(x => x.Id == rateId);
        if (rate is null) return Result<PayRateDto>.Failure(PayrollErrors.NotFound);
        if (rate.IsVoided) return Result<PayRateDto>.Failure(PayrollErrors.AlreadyVoided);
        rate.Void(reason!, actor.UserId, clock.UtcNow);
        if (!await SaveAsync(token)) return Result<PayRateDto>.Failure(PayrollErrors.ConcurrentChange);
        await guard.CommitAsync(token);
        var teachers = (await reader.ListTeachersAsync(centerId, token)).ToDictionary(x => x.Id);
        var classes = (await reader.ListClassesAsync(centerId, token)).ToDictionary(x => x.Id);
        var names = await users.GetDisplayNamesAsync([rate.CreatedByUserId], token);
        return Result<PayRateDto>.Success(RateDto(rate, teachers, classes, names));
    }

    public Task<Result<PayrollMonthDto>> AddPayoutAsync(Guid centerId, string monthText, Guid teacherId, long amount,
        string? methodText, DateOnly? paidOn, string? note, Guid? requestId, PayrollActor actor, CancellationToken token)
    {
        if (!actor.IsOwner) return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden));
        var today = FinanceTime.LocalDate(clock.UtcNow);
        var day = paidOn ?? today;
        if (!Enum.TryParse<PaymentMethod>(methodText, out var method) || !PayrollPayout.IsValid(amount, method, note) ||
            day > today || day < today.AddYears(-1))
            return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidPayout));
        return WriteAsync(centerId, monthText, async (book, month) =>
        {
            if (requestId is { } rid && book.Payouts.Any(x => x.RequestId == rid)) return Error.None;
            if (!book.Teachers.ContainsKey(teacherId)) return PayrollErrors.NotFound;
            if (book.IsClosed(month))
            {
                var line = book.LinesOf(month).FirstOrDefault(x => x.TeacherId == teacherId);
                if (line is null || amount > line.Remaining) return PayrollErrors.PayoutTooLarge;
            }

            var number = await store.NextPayoutNumberAsync(centerId, token);
            await store.AddAsync(PayrollPayout.Create(centerId, number, month, teacherId, amount, method, day, note, requestId,
                actor.UserId, clock.UtcNow), token);
            return Error.None;
        }, actor, token, allowClosed: true);
    }

    public Task<Result<PayrollMonthDto>> VoidPayoutAsync(Guid centerId, Guid payoutId, string? reason, PayrollActor actor,
        CancellationToken token) =>
        VoidEntryAsync(centerId, reason, actor, actor.IsOwner, book => book.Payouts.FirstOrDefault(x => x.Id == payoutId),
            entry => ((PayrollPayout)entry).Month, token, allowClosed: true);

    public Task<Result<PayrollMonthDto>> AddDisputeAsync(Guid centerId, string monthText, string? message, PayrollActor actor,
        CancellationToken token)
    {
        if (!PayrollText.IsReason(message)) return Task.FromResult(Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidReason));
        return WriteAsync(centerId, monthText, async (book, month) =>
        {
            var mine = book.Teachers.Values.FirstOrDefault(x => x.UserId == actor.UserId);
            if (mine is null) return PayrollErrors.Forbidden;
            await store.AddAsync(PayrollDispute.Create(centerId, month, mine.Id, message!, actor.UserId, clock.UtcNow), token);
            return Error.None;
        }, actor, token);
    }

    public async Task<Result<PayrollMonthDto>> ResolveDisputeAsync(Guid centerId, Guid disputeId, string? note,
        PayrollActor actor, CancellationToken token)
    {
        if (!actor.SeesAll) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        if (!PayrollText.IsReason(note)) return Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidReason);
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var book = await LoadAsync(centerId, token);
        var dispute = book.Disputes.FirstOrDefault(x => x.Id == disputeId);
        if (dispute is null) return Result<PayrollMonthDto>.Failure(PayrollErrors.NotFound);
        if (!dispute.IsOpen) return Result<PayrollMonthDto>.Failure(PayrollErrors.DisputeResolved);
        dispute.Resolve(note!, actor.UserId, clock.UtcNow);
        return await FinishAsync(centerId, dispute.Month, guard, actor, token);
    }

    // ---------------- helpers ----------------

    private delegate Task<Error> Change(PayrollBook book, DateOnly month);

    private async Task<Result<PayrollMonthDto>> WriteAsync(Guid centerId, string monthText, Change change, PayrollActor actor,
        CancellationToken token, bool allowClosed = false)
    {
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var book = await LoadAsync(centerId, token);
        if (!book.IsEnabledNow) return Result<PayrollMonthDto>.Failure(PayrollErrors.Disabled);
        if (!InScope(book, monthText, out var month)) return Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidMonth);
        if (!allowClosed && book.IsClosed(month)) return Result<PayrollMonthDto>.Failure(PayrollErrors.MonthClosed);
        var error = await change(book, month);
        if (error != Error.None) return Result<PayrollMonthDto>.Failure(error);
        return await FinishAsync(centerId, month, guard, actor, token);
    }

    private async Task<Result<PayrollMonthDto>> VoidEntryAsync(Guid centerId, string? reason, PayrollActor actor, bool allowed,
        Func<PayrollBook, VoidablePayrollEntry?> find, Func<VoidablePayrollEntry, DateOnly> monthOf, CancellationToken token,
        bool allowClosed = false)
    {
        if (!allowed) return Result<PayrollMonthDto>.Failure(PayrollErrors.Forbidden);
        if (!PayrollText.IsReason(reason)) return Result<PayrollMonthDto>.Failure(PayrollErrors.InvalidReason);
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var book = await LoadAsync(centerId, token);
        if (!book.IsEnabledNow) return Result<PayrollMonthDto>.Failure(PayrollErrors.Disabled);
        var entry = find(book);
        if (entry is null) return Result<PayrollMonthDto>.Failure(PayrollErrors.NotFound);
        if (entry.IsVoided) return Result<PayrollMonthDto>.Failure(PayrollErrors.AlreadyVoided);
        var month = monthOf(entry);
        if (!allowClosed && book.IsClosed(month)) return Result<PayrollMonthDto>.Failure(PayrollErrors.MonthClosed);
        entry.Void(reason!, actor.UserId, clock.UtcNow);
        return await FinishAsync(centerId, month, guard, actor, token);
    }

    private async Task<Result<PayrollMonthDto>> FinishAsync(Guid centerId, DateOnly month, ITuitionGuard guard,
        PayrollActor actor, CancellationToken token)
    {
        if (!await SaveAsync(token)) return Result<PayrollMonthDto>.Failure(PayrollErrors.ConcurrentChange);
        var book = await LoadAsync(centerId, token, sync: false);
        await guard.CommitAsync(token);
        Guid? only = actor.SeesAll ? null : book.Teachers.Values.FirstOrDefault(x => x.UserId == actor.UserId)?.Id;
        return Result<PayrollMonthDto>.Success(await MonthDtoAsync(book, month, only, token));
    }

    private async Task<bool> SaveAsync(CancellationToken token)
    {
        try
        {
            await store.SaveAsync(token);
            return true;
        }
        catch (PayrollPersistenceException)
        {
            return false;
        }
    }

    private async Task<bool> EnabledAsync(Guid centerId, CancellationToken token)
    {
        var current = PayMonth.Of(FinanceTime.LocalDate(clock.UtcNow));
        var change = (await store.ListConfigChangesAsync(centerId, token)).OrderBy(x => x.CreatedAtUtc)
            .LastOrDefault(x => x.EffectiveFrom <= current);
        return change?.Enabled ?? false;
    }

    private static bool InScope(PayrollBook book, string monthText, out DateOnly month) =>
        PayMonth.TryParse(monthText, out month) && book.MonthsInScope().Contains(month);

    private async Task<PayrollBook> ReadAsync(Guid centerId, CancellationToken token)
    {
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var book = await LoadAsync(centerId, token);
        await guard.CommitAsync(token);
        return book;
    }

    /// <summary>Inside the guard. M1 reads receipts, so tuition billing is brought up to date first.</summary>
    private async Task<PayrollBook> LoadAsync(Guid centerId, CancellationToken token, bool sync = true)
    {
        var now = clock.UtcNow;
        var changes = await store.ListConfigChangesAsync(centerId, token);
        IReadOnlyList<Receipt> receipts = [];
        IReadOnlyList<TuitionCharge> charges = [];
        IReadOnlyList<PayrollSessionRow> sessions = [];
        if (changes.Any(x => x.Enabled))
        {
            if (changes.Any(x => x.Mode == PayrollMode.RevenueShare))
            {
                var ledger = await CenterLedger.LoadAsync(centerId, tuition, tuitionReader, token);
                if (sync)
                {
                    ledger.Sync(FinanceTime.LocalDate(now), now);
                    await TuitionWorkflow.PersistAsync(ledger, tuition, token);
                }

                receipts = ledger.Receipts;
                charges = ledger.Charges;
            }

            // Cycles around a month reach back and ahead of it; one extra quarter on both sides covers them.
            var first = changes.Where(x => x.Enabled).Min(x => x.EffectiveFrom).AddMonths(-3);
            var from = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7));
            sessions = await reader.ListSessionsAsync(centerId, from, now.AddMonths(4), token);
        }

        return new PayrollBook(centerId, now, changes, await reader.ListTeachersAsync(centerId, token),
            await reader.ListClassesAsync(centerId, token), sessions, await store.ListRatesAsync(centerId, token),
            await store.ListAdjustmentsAsync(centerId, token), await store.ListExpensesAsync(centerId, token),
            await store.ListPayoutsAsync(centerId, token), await store.ListDisputesAsync(centerId, token),
            await store.ListClosedMonthsAsync(centerId, token), await store.ListCarriesAsync(centerId, token),
            await store.ListEventsAsync(centerId, token), receipts, charges);
    }

    private async Task<PayrollMonthDto> MonthDtoAsync(PayrollBook book, DateOnly month, Guid? only, CancellationToken token)
    {
        var figures = book.Figures(month);
        var closed = book.Closed.GetValueOrDefault(month);
        var carried = book.CarriedInto(month);
        bool Mine(Guid teacherId) => only is null || only == teacherId;
        string TeacherName(Guid id) => book.Teachers.GetValueOrDefault(id)?.FullName ?? "—";
        string ClassCode(Guid id) => book.Classes.GetValueOrDefault(id)?.Code ?? "—";

        var adjustments = book.Adjustments.Where(x => x.Month == month && Mine(x.TeacherId)).ToArray();
        var expenses = only is null ? book.Expenses.Where(x => x.Month == month).ToArray() : [];
        var payouts = book.Payouts.Where(x => x.Month == month && Mine(x.TeacherId)).ToArray();
        var disputes = book.Disputes.Where(x => x.Month == month && Mine(x.TeacherId)).ToArray();
        var events = only is null ? book.Events.Where(x => x.Month == month).ToArray() : [];
        var userIds = adjustments.Select(x => x.CreatedByUserId).Concat(expenses.Select(x => x.CreatedByUserId))
            .Concat(payouts.Select(x => x.CreatedByUserId)).Concat(disputes.Where(x => x.ResolvedByUserId.HasValue)
                .Select(x => x.ResolvedByUserId!.Value))
            .Concat(events.Select(x => x.ActorUserId)).Concat(closed is null ? [] : [closed.ClosedByUserId]).Distinct();
        var names = await users.GetDisplayNamesAsync(userIds, token);
        var closedAt = closed?.ClosedAtUtc;

        var lines = book.LinesOf(month).Where(x => Mine(x.TeacherId)).Select(line =>
        {
            figures.Teachers.TryGetValue(line.TeacherId, out var earned);
            var teacher = book.Teachers.GetValueOrDefault(line.TeacherId);
            return new PayrollLineDto
            {
                TeacherId = line.TeacherId,
                TeacherCode = teacher?.Code ?? "—",
                TeacherName = teacher?.FullName ?? "—",
                Sessions = line.Sessions, Minutes = line.Minutes, Base = line.Base, Adjustments = line.Adjustments,
                Carried = line.Carried, Net = line.Net, Paid = line.Paid, Remaining = line.Remaining,
                Classes = earned is null ? [] : earned.Classes.Values.Select(c => new PayrollClassLineDto
                {
                    ClassId = c.ClassId, ClassCode = ClassCode(c.ClassId),
                    ClassName = book.Classes.GetValueOrDefault(c.ClassId)?.Name ?? "—",
                    Sessions = c.Sessions, Minutes = c.Minutes, Amount = (long)Math.Round(c.Amount, MidpointRounding.AwayFromZero)
                }).OrderBy(c => c.ClassCode, StringComparer.CurrentCultureIgnoreCase).ToList(),
                Items = earned is null ? [] : earned.Items.OrderBy(i => i.StartUtc).Select(i => Item(i, ClassCode)).ToList(),
                CarriedFrom = carried.Where(c => c.TeacherId == line.TeacherId).GroupBy(c => c.From)
                    .Select(g => new PayrollCarriedDto { FromMonth = PayMonth.Format(g.Key), Amount = g.Sum(c => c.Amount) }).ToList()
            };
        }).OrderByDescending(x => x.Net).ThenBy(x => x.TeacherName, StringComparer.CurrentCulture).ToList();

        return new PayrollMonthDto
        {
            Month = PayMonth.Format(month),
            Mode = book.ModeOf(month).ToString(),
            Status = closed is null ? "Draft" : "Closed",
            ClosedAtUtc = closedAt,
            ClosedByName = closed is null ? null : names.GetValueOrDefault(closed.ClosedByUserId),
            CloseBlockers = only is null && closed is null ? book.CloseBlockers(month).ToList() : [],
            CanReopen = only is null && book.LatestClosed == month,
            Pool = only is not null || figures.Mode != PayrollMode.RevenueShare ? null : new PayrollPoolDto
            {
                Collected = figures.Collected, Prepaid = figures.Prepaid, Unassigned = figures.UnassignedTotal,
                Expenses = figures.Expenses, Distributable = figures.Distributable,
                UnassignedItems = figures.Unassigned.GroupBy(x => (x.ClassId, x.PeriodStart, x.PeriodEnd))
                    .Select(g => new PayrollUnassignedDto
                    {
                        ClassId = g.Key.ClassId, ClassCode = ClassCode(g.Key.ClassId), PeriodStart = g.Key.PeriodStart,
                        PeriodEnd = g.Key.PeriodEnd, Amount = g.Sum(x => x.Amount)
                    }).ToList()
            },
            Lines = lines,
            Adjustments = adjustments.OrderByDescending(x => x.CreatedAtUtc).Select(x => new PayrollAdjustmentDto
            {
                Id = x.Id, TeacherId = x.TeacherId, TeacherName = TeacherName(x.TeacherId), Amount = x.Amount, Reason = x.Reason,
                CreatedByName = names.GetValueOrDefault(x.CreatedByUserId), CreatedAtUtc = x.CreatedAtUtc,
                VoidedAtUtc = x.VoidedAtUtc, VoidReason = x.VoidReason
            }).ToList(),
            Expenses = expenses.OrderBy(x => x.SpentOn).Select(x => new SharedExpenseDto
            {
                Id = x.Id, SpentOn = x.SpentOn, Category = x.Category, Amount = x.Amount, Note = x.Note,
                CreatedByName = names.GetValueOrDefault(x.CreatedByUserId), VoidedAtUtc = x.VoidedAtUtc, VoidReason = x.VoidReason
            }).ToList(),
            Payouts = payouts.OrderByDescending(x => x.Number).Select(x => new PayrollPayoutDto
            {
                Id = x.Id, NumberText = PayrollPayout.FormatNumber(x.Number), Month = PayMonth.Format(x.Month),
                TeacherId = x.TeacherId, TeacherName = TeacherName(x.TeacherId), Amount = x.Amount, Method = x.Method.ToString(),
                PaidOn = x.PaidOn, Note = x.Note, Advance = closedAt is null || x.CreatedAtUtc < closedAt,
                CreatedByName = names.GetValueOrDefault(x.CreatedByUserId), CreatedAtUtc = x.CreatedAtUtc,
                VoidedAtUtc = x.VoidedAtUtc, VoidReason = x.VoidReason
            }).ToList(),
            Disputes = disputes.OrderByDescending(x => x.CreatedAtUtc).Select(x => new PayrollDisputeDto
            {
                Id = x.Id, TeacherId = x.TeacherId, TeacherName = TeacherName(x.TeacherId), Message = x.Message,
                CreatedAtUtc = x.CreatedAtUtc, ResolvedAtUtc = x.ResolvedAtUtc,
                ResolvedByName = x.ResolvedByUserId is { } r ? names.GetValueOrDefault(r) : null, Resolution = x.Resolution
            }).ToList(),
            Unsubmitted = closed is not null ? [] : figures.Unsubmitted
                .Where(x => Mine(x.TeacherId)).OrderBy(x => x.StartUtc).Select(x => new PayrollUnsubmittedDto
                {
                    SessionId = x.Id, ClassCode = ClassCode(x.ClassId), StartUtc = x.StartUtc, TeacherName = TeacherName(x.TeacherId)
                }).ToList(),
            MissingRates = closed is not null ? [] : figures.MissingRates
                .Where(x => only is null || figures.Teachers.GetValueOrDefault(only.Value)?.Items.Contains(x) == true)
                .Select(x => Item(x, ClassCode)).ToList(),
            Events = events.OrderByDescending(x => x.CreatedAtUtc).Select(x => new PayrollEventDto
            {
                Kind = x.Kind.ToString(), Reason = x.Reason, ActorName = names.GetValueOrDefault(x.ActorUserId),
                CreatedAtUtc = x.CreatedAtUtc
            }).ToList()
        };
    }

    private static PayrollSessionItemDto Item(SessionFigure item, Func<Guid, string> classCode) => new()
    {
        SessionId = item.SessionId, ClassId = item.ClassId, ClassCode = classCode(item.ClassId), StartUtc = item.StartUtc,
        Minutes = item.Minutes, Amount = item.Amount, Substitute = item.Substitute
    };

    private static PayRateDto RateDto(PayRate rate, IReadOnlyDictionary<Guid, PayrollTeacherRow> teachers,
        IReadOnlyDictionary<Guid, PayrollClassRow> classes, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = rate.Id, TeacherId = rate.TeacherId, TeacherName = teachers.GetValueOrDefault(rate.TeacherId)?.FullName ?? "—",
        ClassId = rate.ClassId, ClassCode = rate.ClassId is { } c ? classes.GetValueOrDefault(c)?.Code : null,
        Amount = rate.Amount, EffectiveFrom = rate.EffectiveFrom, CreatedByName = names.GetValueOrDefault(rate.CreatedByUserId),
        CreatedAtUtc = rate.CreatedAtUtc, VoidedAtUtc = rate.VoidedAtUtc, VoidReason = rate.VoidReason
    };
}
