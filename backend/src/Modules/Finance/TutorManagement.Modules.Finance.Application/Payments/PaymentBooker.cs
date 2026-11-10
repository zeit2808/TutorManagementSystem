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
}

/// <summary>
/// Round 3a of docs/THANH-TOAN-QR-KE-HOACH.md: matches pulled transfers of centers in Shadow and records what would be
/// booked (<c>shadow_*</c>), so it can be compared with the receipts staff record by hand before Auto is switched on.
/// Never creates a receipt.
/// </summary>
public sealed class PaymentBooker(IPaymentBookingStore store, ITuitionAcademicsReader academics, IClock clock)
{
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

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

    private static string Describe(Exception error) => $"{error.GetType().Name}: {error.Message}";
}
