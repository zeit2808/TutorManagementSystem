using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Application.Payments;

public interface IPaymentBookingStore
{
    /// <summary>
    /// Leases up to <paramref name="max"/> Received rows of centers in Shadow that were not evaluated yet
    /// (FOR UPDATE SKIP LOCKED, oldest first, respecting next_attempt_at). Returned untracked.
    /// </summary>
    Task<IReadOnlyList<BankTransaction>> ClaimShadowAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max,
        CancellationToken token);

    /// <summary>The center's receiving account, the accounts its QR codes were made for, and the QR codes in <paramref name="codes"/>.</summary>
    Task<(string? ConfigAccount, IReadOnlyCollection<string> IntentAccounts, IReadOnlyList<IntentRef> Intents)> LoadIntentsAsync(
        Guid centerId, Guid configId, IReadOnlyCollection<string> codes, CancellationToken token);

    /// <summary>Writes the expected result; false if the lease was lost. The row stays Received.</summary>
    Task<bool> SaveShadowAsync(Guid transactionId, Guid leaseId, MatchDecision decision, DateTimeOffset now, CancellationToken token);

    /// <summary>Releases the lease and schedules another try (1, 5, 15, 60 minutes).</summary>
    Task FailAsync(Guid transactionId, Guid leaseId, string error, DateTimeOffset now, CancellationToken token);

    /// <summary>Leases Received rows of centers in Auto (FOR UPDATE SKIP LOCKED, oldest first); returns their ids.</summary>
    Task<IReadOnlyList<Guid>> ClaimAutoAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max, CancellationToken token);

    /// <summary>Tracked rows, in the same unit of work as the tuition store, so booking commits with the receipt.</summary>
    Task<IReadOnlyList<BankTransaction>> LoadTrackedAsync(IReadOnlyCollection<Guid> ids, CancellationToken token);

    /// <summary>Tracked QR codes of the center, to mark them paid and to pay their charges first.</summary>
    Task<IReadOnlyList<Domain.Tuition.PaymentIntent>> LoadIntentsTrackedAsync(Guid centerId, IReadOnlyCollection<Guid> ids,
        CancellationToken token);
}

/// <summary>
/// Round 3a of docs/THANH-TOAN-QR-KE-HOACH.md: matches pulled transfers of centers in Shadow and records what would be
/// booked (<c>shadow_*</c>), so it can be compared with the receipts staff record by hand before Auto is switched on.
/// Never creates a receipt.
/// </summary>
public sealed class PaymentBooker(IPaymentBookingStore store, ITuitionAcademicsReader academics, IClock clock,
    ITuitionStore tuition)
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Round 3b: books Received transfers of centers in Auto. One center at a time inside its ledger lock: the ledger
    /// is loaded once, every receipt, allocation and status change commits together, and the two unique indexes on
    /// receipts make a second booking of the same transfer impossible. A batch that fails is retried row by row so one
    /// bad row does not hold the others.
    /// </summary>
    public async Task<int> RunAutoOnceAsync(int max, CancellationToken token)
    {
        var leaseId = Guid.NewGuid();
        var ids = await store.ClaimAutoAsync(leaseId, clock.UtcNow, Lease, max, token);
        if (ids.Count == 0) return 0;
        var rows = await store.LoadTrackedAsync(ids, token);
        var groups = rows.GroupBy(x => (x.CenterId, x.ConfigId)).Select(x => (x.Key, Ids: x.Select(r => r.Id).ToArray())).ToArray();
        tuition.Detach();
        foreach (var (key, batch) in groups)
        {
            try { await BookCenterAsync(key.CenterId, key.ConfigId, batch, leaseId, token); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                tuition.Detach();
                foreach (var id in batch)
                {
                    try { await BookCenterAsync(key.CenterId, key.ConfigId, [id], leaseId, token); }
                    catch (Exception single) when (single is not OperationCanceledException)
                    {
                        tuition.Detach();
                        await store.FailAsync(id, leaseId, Describe(single), clock.UtcNow, token);
                    }
                }
            }
            tuition.Detach();
        }
        return ids.Count;
    }

    private async Task BookCenterAsync(Guid centerId, Guid configId, IReadOnlyCollection<Guid> ids, Guid leaseId,
        CancellationToken token)
    {
        await using var guard = await tuition.BeginCenterGuardAsync(centerId, token);
        var now = clock.UtcNow;
        var ledger = await CenterLedger.LoadAsync(centerId, tuition, academics, token);
        ledger.Sync(FinanceTime.LocalDate(now), now);
        var config = await tuition.FindProviderConfigAsync(centerId, PaymentProvider.Sepay, token);
        var rows = (await store.LoadTrackedAsync(ids, token))
            .Where(x => x.ProcessingId == leaseId && x.Status == BankTransactionStatus.Received).ToArray();
        if (config is null || config.Id != configId || config.Mode != PaymentProviderMode.Auto || rows.Length == 0)
        {
            await guard.CommitAsync(token);
            return;
        }

        var codes = rows.SelectMany(x => Domain.Tuition.PaymentCode.FindIn(x.Content)).Distinct().ToArray();
        var (account, intentAccounts, intents) = await store.LoadIntentsAsync(centerId, configId, codes, token);
        var context = new MatchContext(account, intentAccounts, intents.ToDictionary(x => x.Code, StringComparer.Ordinal),
            [.. ledger.Students]);
        var tracked = (await store.LoadIntentsTrackedAsync(centerId, intents.Select(x => x.Id).ToArray(), token))
            .ToDictionary(x => x.Id);

        var booked = new List<Domain.Tuition.Receipt>();
        foreach (var row in rows.OrderBy(x => x.TransactedAtUtc))
        {
            // K21: before Auto was switched on, staff recorded transfers by hand.
            if (config.AutoSinceUtc is not { } since || row.TransactedAtUtc < since)
            {
                row.IgnoreByRule(IgnoreReason.BeforeAuto);
                continue;
            }
            var decision = PaymentMatcher.Match(row, context);
            switch (decision.Kind)
            {
                case MatchKind.Book:
                    var studentId = decision.StudentId!.Value;
                    var number = await tuition.NextReceiptNumberAsync(centerId, token);
                    var receipt = Domain.Tuition.Receipt.CreateFromBank(centerId, number, studentId, row.Amount,
                        FinanceTime.LocalDate(row.TransactedAtUtc), Note(row), row.Id, Domain.Tuition.ReceiptSource.Auto,
                        Domain.Tuition.SystemActors.PaymentFeed, now);
                    ledger.Receipts.Add(receipt);
                    await tuition.AddAsync(receipt, token);
                    var first = decision.IntentId is { } intentId && tracked.TryGetValue(intentId, out var intent)
                        ? intent.ChargeIds : [];
                    var open = ledger.OpenCharges(studentId).Select(x => x.Id).ToHashSet();
                    ledger.Spread(receipt, first.Where(open.Contains).ToArray(), now);
                    if (decision.IntentId is { } paid && tracked.TryGetValue(paid, out var paidIntent)) paidIntent.MarkPaid(now);
                    row.MarkBooked(studentId, decision.IntentId, now);
                    booked.Add(receipt);
                    break;
                case MatchKind.Review:
                    row.SendToReview(decision.Review!.Value);
                    break;
                default:
                    row.IgnoreByRule(decision.Ignore!.Value);
                    break;
            }
        }
        // D6: the confirmation is queued with the receipts, so it is sent only if they commit.
        await PaymentEmails.QueueAsync(tuition, academics, ledger, booked, token);
        await TuitionWorkflow.PersistAsync(ledger, tuition, token);
        await guard.CommitAsync(token);
    }

    private static string Note(BankTransaction row)
    {
        var text = $"Chuyển khoản {row.BankCode} {row.ReferenceCode ?? row.ProviderTxnId}: {row.Content}".Trim();
        return text.Length <= Domain.Tuition.Receipt.MaxNoteLength ? text : text[..Domain.Tuition.Receipt.MaxNoteLength];
    }

    /// <summary>Evaluates due rows; returns how many were claimed.</summary>
    public async Task<int> RunShadowOnceAsync(int max, CancellationToken token)
    {
        var leaseId = Guid.NewGuid();
        var rows = await store.ClaimShadowAsync(leaseId, clock.UtcNow, Lease, max, token);
        foreach (var center in rows.GroupBy(x => (x.CenterId, x.ConfigId)))
        {
            MatchContext context;
            try
            {
                var codes = center.SelectMany(x => Domain.Tuition.PaymentCode.FindIn(x.Content)).Distinct().ToArray();
                var (account, intentAccounts, intents) = await store.LoadIntentsAsync(center.Key.CenterId, center.Key.ConfigId, codes, token);
                context = new MatchContext(account, intentAccounts, intents.ToDictionary(x => x.Code, StringComparer.Ordinal),
                    await academics.ListStudentsAsync(center.Key.CenterId, token));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                foreach (var row in center) await store.FailAsync(row.Id, leaseId, Describe(error), clock.UtcNow, token);
                continue;
            }

            foreach (var row in center)
            {
                try { await store.SaveShadowAsync(row.Id, leaseId, PaymentMatcher.Match(row, context), clock.UtcNow, token); }
                catch (Exception error) when (error is not OperationCanceledException)
                { await store.FailAsync(row.Id, leaseId, Describe(error), clock.UtcNow, token); }
            }
        }
        return rows.Count;
    }

    /// <summary>With the inner messages, so a unique conflict on receipts (Đợt 5 alert) is visible by constraint name.</summary>
    private static string Describe(Exception error)
    {
        var parts = new List<string>();
        for (var current = error; current is not null && parts.Count < 4; current = current.InnerException)
            parts.Add($"{current.GetType().Name}: {current.Message}");
        return string.Join(" → ", parts);
    }
}
