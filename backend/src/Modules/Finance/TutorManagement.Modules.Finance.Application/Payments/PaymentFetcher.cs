using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>A config the Worker has leased for one pull, with the schedule as it was when claimed.</summary>
public sealed record PullTarget(
    Guid ConfigId,
    Guid CenterId,
    PaymentProvider Provider,
    byte[] ApiToken,
    string KeyId,
    string? Cursor,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? PullRequestedAtUtc,
    DateTimeOffset? LastPulledAtUtc,
    int PullRetryCount);

/// <summary>What one pull did to the inbox.</summary>
public sealed record PullResult(int Inserted, int SeenAgain, int Held, int Changed);

/// <summary>When to pull this config next, decided by the fetcher.</summary>
public sealed record PullSchedule(DateTimeOffset NextPullAtUtc, int RetryCount);

public interface IPaymentPullStore
{
    /// <summary>
    /// Leases up to <paramref name="max"/> configs that are due (not Off, next_pull_at passed, last pull at least a second
    /// ago, no live lease). FOR UPDATE SKIP LOCKED: two Workers never pull the same config.
    /// </summary>
    Task<IReadOnlyList<PullTarget>> ClaimDueAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max, CancellationToken token);

    /// <summary>
    /// One transaction: stores the incoming transactions (both keys, held duplicates), moves the cursor and the schedule.
    /// Returns null, changing nothing, if the lease was lost. A doorbell that arrived during the pull still wins.
    /// </summary>
    Task<PullResult?> SaveAsync(PullTarget target, Guid leaseId, IReadOnlyList<ReceivedBankTransaction> incoming,
        FetchedBy fetchedBy, string? newCursor, PullSchedule schedule, DateTimeOffset now, CancellationToken token);

    /// <summary>Releases the lease, records the error and when to try again. Nothing is stored or skipped.</summary>
    Task FailAsync(PullTarget target, Guid leaseId, string error, DateTimeOffset nextPullAtUtc, DateTimeOffset now,
        CancellationToken token);
}

/// <summary>
/// Pulls new transactions for due configs (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2e): the provider API is the only
/// source of money; webhooks only move <c>next_pull_at</c>. The cursor moves only after the rows are stored, so a crash
/// never loses a transaction, and repeats are absorbed by the two unique keys.
/// </summary>
public sealed class PaymentFetcher(IPaymentPullStore store, IEnumerable<IBankFeedProvider> providers, ISecretProtector protector,
    IClock clock)
{
    public static readonly TimeSpan Periodic = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan[] DoorbellRetries =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];
    public const int PerPage = 100;
    public const int MaxPages = 50;

    /// <summary>Keeps page requests of one pull under SePay's 3 requests per second.</summary>
    public static readonly TimeSpan PageSpacing = TimeSpan.FromMilliseconds(350);

    private readonly Dictionary<PaymentProvider, IBankFeedProvider> _providers = providers.ToDictionary(x => x.Provider);

    /// <summary>Pulls every config that is due now; returns how many were pulled (successfully or not).</summary>
    public async Task<int> RunOnceAsync(int max, CancellationToken token)
    {
        var leaseId = Guid.NewGuid();
        var targets = await store.ClaimDueAsync(leaseId, clock.UtcNow, Lease, max, token);
        foreach (var target in targets) await PullAsync(target, leaseId, token);
        return targets.Count;
    }

    private async Task PullAsync(PullTarget target, Guid leaseId, CancellationToken token)
    {
        // A doorbell rang since the last pull, or we are still in its 1/3/10/30 s retry chain.
        var doorbell = target.PullRetryCount > 0 ||
                       target.PullRequestedAtUtc is { } rang && (target.LastPulledAtUtc is not { } pulled || rang > pulled);
        List<ReceivedBankTransaction> received = [];
        string? newest = null;
        var complete = false;
        try
        {
            if (!_providers.TryGetValue(target.Provider, out var provider))
                throw new BankFeedException($"No feed for {target.Provider}.", false);
            var apiToken = protector.Unprotect(target.ApiToken, target.KeyId,
                SecretPurposes.ApiToken(target.CenterId, target.Provider.ToString()));
            for (var page = 1; page <= MaxPages; page++)
            {
                if (page > 1) await Task.Delay(PageSpacing, token);
                var result = await provider.ListAsync(apiToken, new BankFeedQuery(target.Cursor,
                    target.Cursor is null ? target.CreatedAtUtc : null, page, PerPage), token);
                if (page == 1 && result.Transactions.Count > 0) newest = result.Transactions[0].ProviderTxnId;
                received.AddRange(result.Transactions);
                if (!result.HasMore) { complete = true; break; }
            }
        }
        catch (BankFeedException error)
        {
            var wait = error.RetryAfter ?? (error.Retryable ? TimeSpan.FromMinutes(1) : Periodic);
            await store.FailAsync(target, leaseId, error.Message, clock.UtcNow.Add(wait), clock.UtcNow, token);
            return;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            await store.FailAsync(target, leaseId, "The stored API token cannot be decrypted with the configured keys.",
                clock.UtcNow.Add(Periodic), clock.UtcNow, token);
            return;
        }

        var incoming = received.Where(x => x.Direction == TransferDirection.In).ToArray();
        var now = clock.UtcNow;
        // A pull that stopped at the page limit keeps the old cursor: newest-first pages mean the oldest new rows were
        // not read yet. The rows read so far are stored; repeats are absorbed by the keys.
        var cursor = complete ? newest ?? target.Cursor : target.Cursor;
        var schedule = !complete
            ? new PullSchedule(now, 0)
            : doorbell && received.Count == 0 && target.PullRetryCount < DoorbellRetries.Length
                ? new PullSchedule(now.Add(DoorbellRetries[target.PullRetryCount]), target.PullRetryCount + 1)
                : new PullSchedule(now.Add(Periodic), 0);
        await store.SaveAsync(target, leaseId, incoming, doorbell ? FetchedBy.Doorbell : FetchedBy.Periodic, cursor, schedule,
            now, token);
    }
}
