using System.Text.Json;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>A student staff may want to pick, and why. Never applied by itself (D1).</summary>
public sealed class TransferSuggestionDto
{
    public Guid StudentId { get; init; }
    public string StudentCode { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class BankTransferDto
{
    public Guid Id { get; init; }
    public DateTimeOffset TransactedAtUtc { get; init; }
    public long Amount { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ReferenceCode { get; init; }
    public string BankCode { get; init; } = string.Empty;
    public string AccountNumber { get; init; } = string.Empty;
    public string? VirtualAccount { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ReviewReason { get; init; }
    public string? IgnoreReason { get; init; }
    public Guid? StudentId { get; init; }
    public string? StudentCode { get; init; }
    public string? StudentName { get; init; }
    public Guid? ReceiptId { get; init; }
    public string? ReceiptNumber { get; init; }
    public Guid? DuplicateOfId { get; init; }
    public string? LastError { get; init; }
    public List<TransferSuggestionDto> Suggestions { get; init; } = [];
    public int Version { get; init; }
}

public static class QueueErrors
{
    public static readonly Error NotFound = new("payment_transfer_not_found", "The bank transfer was not found.");
    public static readonly Error NotInQueue = new("payment_transfer_not_in_queue",
        "The transfer is no longer waiting for this action; reload the list.");
    public static readonly Error Version = new("tuition_concurrent_change",
        "The transfer was changed by someone else. Reload and try again.", "expectedVersion");
    public static readonly Error InvalidReason = new("validation_error", "A reason of 3–300 characters is required.", "reason");
    public static readonly Error InvalidStatus = new("validation_error",
        "status must be Queue, NeedsReview, Held, Failed, Received, Booked, Ignored or All.", "status");
}

/// <summary>
/// Đợt 4 of docs/THANH-TOAN-QR-KE-HOACH.md: "Tiền chưa rõ của ai". Staff answer one question — whose money is it —
/// helped by suggestions; owners and managers may say it is not tuition; the owner decides held copies and retries.
/// Every change is audited and goes through the center's ledger lock.
/// </summary>
public sealed class PaymentQueueWorkflow(ITuitionStore store, ITuitionAcademicsReader academics, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Statuses = ["Queue", "NeedsReview", "Held", "Failed", "Received", "Booked", "Ignored", "All"];

    public async Task<Result<TuitionPage<BankTransferDto>>> ListAsync(Guid centerId, string status, int page, int pageSize,
        CancellationToken token)
    {
        BankTransactionStatus[]? wanted = status switch
        {
            "Queue" => [BankTransactionStatus.NeedsReview, BankTransactionStatus.Held, BankTransactionStatus.Failed],
            "All" => null,
            _ when Enum.TryParse<BankTransactionStatus>(status, false, out var one) && Enum.IsDefined(one) && Statuses.Contains(status) => [one],
            _ => []
        };
        if (wanted is { Length: 0 }) return Result<TuitionPage<BankTransferDto>>.Failure(QueueErrors.InvalidStatus);
        var (items, total) = await store.ListBankTransfersPageAsync(centerId, wanted, page, pageSize, token);
        var students = (await academics.ListStudentsAsync(centerId, token)).ToDictionary(x => x.Id);
        var intents = await store.ListPendingIntentsOfCenterAsync(centerId, token);
        var receipts = await store.ListLiveReceiptsOfTransfersAsync(items.Select(x => x.Id).ToArray(), token);
        return Result<TuitionPage<BankTransferDto>>.Success(new TuitionPage<BankTransferDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = items.Select(x => View(x, students, intents, receipts)).ToList()
        });
    }

    /// <summary>Whose money: a receipt for this student, paying <paramref name="chargeIds"/> first, then the oldest charges.</summary>
    public async Task<Result<BankTransferDto>> AssignAsync(Guid centerId, Guid transferId, Guid studentId,
        IReadOnlyList<Guid>? chargeIds, int expectedVersion, Guid actorId, CancellationToken token)
    {
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var transfer = await store.FindBankTransactionAsync(transferId, token);
        if (transfer is null || transfer.CenterId != centerId) return Result<BankTransferDto>.Failure(QueueErrors.NotFound);
        if (transfer.Version != expectedVersion) return Result<BankTransferDto>.Failure(QueueErrors.Version);
        if (transfer.Status != BankTransactionStatus.NeedsReview) return Result<BankTransferDto>.Failure(QueueErrors.NotInQueue);
        if (transfer.Amount > PaymentMatcher.MaxAmount) return Result<BankTransferDto>.Failure(TuitionErrors.InvalidPayment);

        var now = clock.UtcNow;
        var ledger = await CenterLedger.LoadAsync(centerId, store, academics, token);
        ledger.Sync(FinanceTime.LocalDate(now), now);
        var student = ledger.FindStudent(studentId);
        if (student is null) return Result<BankTransferDto>.Failure(TuitionErrors.NotFound);
        if (student.MergedIntoStudentId.HasValue) return Result<BankTransferDto>.Failure(TuitionErrors.StudentUnavailable);
        var first = chargeIds?.Distinct().ToArray() ?? [];
        var open = ledger.OpenCharges(studentId).Select(x => x.Id).ToHashSet();
        if (first.Any(id => !open.Contains(id))) return Result<BankTransferDto>.Failure(TuitionErrors.InvalidCharges);

        var number = await store.NextReceiptNumberAsync(centerId, token);
        var receipt = Receipt.CreateFromBank(centerId, number, studentId, transfer.Amount,
            FinanceTime.LocalDate(transfer.TransactedAtUtc), Note(transfer), transfer.Id, ReceiptSource.Manual, actorId, now);
        ledger.Receipts.Add(receipt);
        await store.AddAsync(receipt, token);
        ledger.Spread(receipt, first, now);
        var before = Snapshot(transfer);
        transfer.AssignManually(studentId, now);
        await store.AddAsync(PaymentAuditEvent.Record(centerId, actorId, "transfer_assigned", "bank_transaction", transfer.Id,
            before, JsonSerializer.Serialize(new { studentId, receipt = Receipt.FormatNumber(number) }, Json), now), token);
        await PaymentEmails.QueueAsync(store, academics, ledger, [receipt], token);
        try { await TuitionWorkflow.PersistAsync(ledger, store, token); }
        catch (TuitionPersistenceException) { return Result<BankTransferDto>.Failure(QueueErrors.Version); }
        await guard.CommitAsync(token);
        return Result<BankTransferDto>.Success(View(transfer, ledger.Students.ToDictionary(x => x.Id), [],
            new Dictionary<Guid, (Guid, int)> { [transfer.Id] = (receipt.Id, receipt.Number) }));
    }

    /// <summary>Owner or manager: not tuition (a refund, a personal transfer…). The reason is kept in the audit trail.</summary>
    public Task<Result<BankTransferDto>> IgnoreAsync(Guid centerId, Guid transferId, string? reason, int expectedVersion,
        Guid actorId, CancellationToken token) =>
        ChangeAsync(centerId, transferId, expectedVersion, actorId, "transfer_ignored", reason, requireReason: true,
            x => x.Status is BankTransactionStatus.NeedsReview or BankTransactionStatus.Failed, x => x.IgnoreByPerson(), token);

    /// <summary>Owner: a held copy is either the same money twice (duplicate) or a second real transfer.</summary>
    public Task<Result<BankTransferDto>> ResolveHeldAsync(Guid centerId, Guid transferId, bool isDuplicate, string? reason,
        int expectedVersion, Guid actorId, CancellationToken token) =>
        ChangeAsync(centerId, transferId, expectedVersion, actorId, isDuplicate ? "held_marked_duplicate" : "held_released",
            reason, requireReason: true, x => x.Status == BankTransactionStatus.Held, x => x.ResolveHeld(isDuplicate), token);

    /// <summary>Owner: retry a transfer that failed five times, after the cause was fixed.</summary>
    public Task<Result<BankTransferDto>> RetryAsync(Guid centerId, Guid transferId, int expectedVersion, Guid actorId,
        CancellationToken token) =>
        ChangeAsync(centerId, transferId, expectedVersion, actorId, "transfer_retried", null, requireReason: false,
            x => x.Status == BankTransactionStatus.Failed, x => x.Retry(), token);

    private async Task<Result<BankTransferDto>> ChangeAsync(Guid centerId, Guid transferId, int expectedVersion, Guid actorId,
        string action, string? reason, bool requireReason, Func<BankTransaction, bool> allowed, Action<BankTransaction> change,
        CancellationToken token)
    {
        var text = reason?.Trim();
        if (requireReason && (text is null || text.Length is < 3 or > Receipt.MaxNoteLength))
            return Result<BankTransferDto>.Failure(QueueErrors.InvalidReason);
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var transfer = await store.FindBankTransactionAsync(transferId, token);
        if (transfer is null || transfer.CenterId != centerId) return Result<BankTransferDto>.Failure(QueueErrors.NotFound);
        if (transfer.Version != expectedVersion) return Result<BankTransferDto>.Failure(QueueErrors.Version);
        if (!allowed(transfer)) return Result<BankTransferDto>.Failure(QueueErrors.NotInQueue);
        var before = Snapshot(transfer);
        change(transfer);
        await store.AddAsync(PaymentAuditEvent.Record(centerId, actorId, action, "bank_transaction", transfer.Id, before,
            JsonSerializer.Serialize(new { status = transfer.Status.ToString(), reason = text }, Json), clock.UtcNow), token);
        try { await store.SaveAsync(token); }
        catch (TuitionPersistenceException) { return Result<BankTransferDto>.Failure(QueueErrors.Version); }
        await guard.CommitAsync(token);
        return Result<BankTransferDto>.Success(View(transfer, new Dictionary<Guid, TuitionStudentRow>(), [],
            new Dictionary<Guid, (Guid Id, int Number)>()));
    }

    private static string Snapshot(BankTransaction x) => JsonSerializer.Serialize(new
    {
        status = x.Status.ToString(), reviewReason = x.ReviewReason?.ToString(), studentId = x.StudentId
    }, Json);

    private static string Note(BankTransaction row)
    {
        var text = $"Chuyển khoản {row.BankCode} {row.ReferenceCode ?? row.ProviderTxnId}: {row.Content}".Trim();
        return text.Length <= Receipt.MaxNoteLength ? text : text[..Receipt.MaxNoteLength];
    }

    private static BankTransferDto View(BankTransaction x, IReadOnlyDictionary<Guid, TuitionStudentRow> students,
        IReadOnlyList<PaymentIntent> intents, IReadOnlyDictionary<Guid, (Guid Id, int Number)> receipts)
    {
        var student = x.StudentId is { } s ? students.GetValueOrDefault(s) : null;
        var receipt = receipts.TryGetValue(x.Id, out var r) ? r : ((Guid Id, int Number)?)null;
        return new BankTransferDto
        {
            Id = x.Id, TransactedAtUtc = x.TransactedAtUtc, Amount = x.Amount, Content = x.Content, ReferenceCode = x.ReferenceCode,
            BankCode = x.BankCode, AccountNumber = x.AccountNumber, VirtualAccount = x.VirtualAccount, Status = x.Status.ToString(),
            ReviewReason = x.ReviewReason?.ToString(), IgnoreReason = x.IgnoreReason?.ToString(), StudentId = x.StudentId,
            StudentCode = student?.Code, StudentName = student?.FullName, ReceiptId = receipt?.Id,
            ReceiptNumber = receipt is { } n ? Receipt.FormatNumber(n.Number) : null, DuplicateOfId = x.DuplicateOfId,
            LastError = x.LastError, Version = x.Version,
            Suggestions = x.Status == BankTransactionStatus.NeedsReview ? Suggest(x, students, intents) : []
        };
    }

    /// <summary>
    /// D1: a QR code waiting for exactly this amount; a QR code one character away from what was typed; a student code
    /// anywhere in the content, even split by spaces. Merged profiles point to the kept one.
    /// </summary>
    public static List<TransferSuggestionDto> Suggest(BankTransaction x, IReadOnlyDictionary<Guid, TuitionStudentRow> students,
        IReadOnlyList<PaymentIntent> intents)
    {
        Guid Resolve(Guid id)
        {
            var current = id;
            for (var hop = 0; hop < 10 && students.TryGetValue(current, out var row) && row.MergedIntoStudentId is { } next; hop++)
                current = next;
            return current;
        }
        var run = new string(x.Content.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        var found = new List<(Guid Student, string Reason)>();
        foreach (var intent in intents.Where(i => i.Amount == x.Amount))
            found.Add((Resolve(intent.StudentId), $"Mã QR {intent.Code} đang chờ đúng số tiền này"));
        foreach (var intent in intents.Where(i => NearCode(run, i.Code)))
            found.Add((Resolve(intent.StudentId), $"Nội dung gần giống mã QR {intent.Code}"));
        foreach (var student in students.Values.Where(s => !s.MergedIntoStudentId.HasValue))
            if (PaymentMatcher.StudentCode(student.Code) is { Length: >= 4 } code && run.Contains(code, StringComparison.Ordinal))
                found.Add((student.Id, $"Nội dung có mã học sinh {student.Code}"));
        return found.GroupBy(f => f.Student)
            .Where(g => students.ContainsKey(g.Key))
            .Select(g => new TransferSuggestionDto
            {
                StudentId = g.Key, StudentCode = students[g.Key].Code, StudentName = students[g.Key].FullName,
                Reason = string.Join("; ", g.Select(f => f.Reason).Distinct())
            })
            .OrderByDescending(s => s.Reason.Count(c => c == ';')).ThenBy(s => s.StudentName, StringComparer.CurrentCulture)
            .Take(5).ToList();
    }

    /// <summary>The content holds "HP" + 9 characters that differ from <paramref name="code"/> in exactly one place.</summary>
    private static bool NearCode(string run, string code)
    {
        for (var at = run.IndexOf("HP", StringComparison.Ordinal); at >= 0 && at + code.Length <= run.Length;
             at = run.IndexOf("HP", at + 1, StringComparison.Ordinal))
        {
            var differences = 0;
            for (var k = 2; k < code.Length && differences < 2; k++)
                if (run[at + k] != code[k]) differences++;
            if (differences == 1) return true;
        }
        return false;
    }
}
