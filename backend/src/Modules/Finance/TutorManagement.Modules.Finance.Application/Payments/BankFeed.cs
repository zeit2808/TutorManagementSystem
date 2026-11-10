using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>
/// One page of transactions. <c>SinceId</c> asks for transactions newer than that provider id (newest first);
/// without it, <c>FromUtc</c> bounds the first pull of a new connection.
/// </summary>
public sealed record BankFeedQuery(string? SinceId, DateTimeOffset? FromUtc, int Page, int PerPage);

/// <summary>Transactions in the provider's order (newest first), both directions.</summary>
public sealed record BankFeedPage(IReadOnlyList<ReceivedBankTransaction> Transactions, bool HasMore);

/// <summary>The provider's transaction API (docs/THANH-TOAN-QR-KE-HOACH.md, T1). The only source that books money.</summary>
public interface IBankFeedProvider
{
    PaymentProvider Provider { get; }

    /// <summary>Throws <see cref="BankFeedException"/> for anything but a readable page.</summary>
    Task<BankFeedPage> ListAsync(string apiToken, BankFeedQuery query, CancellationToken token);
}

/// <summary>
/// A pull that did not finish. <see cref="Retryable"/>: network, 5xx or 429 — try again later.
/// Otherwise (token rejected, unreadable transaction) a person has to look; the pull is still retried, never skipped,
/// so no transaction is lost behind it.
/// </summary>
public sealed class BankFeedException(string message, bool retryable, TimeSpan? retryAfter = null, Exception? inner = null)
    : Exception(message, inner)
{
    public bool Retryable { get; } = retryable;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
