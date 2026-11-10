using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payments;

public enum BankTransactionStatus
{
    Received = 1,
    Booked = 2,
    NeedsReview = 3,
    Held = 4,
    Failed = 5,
    Ignored = 6
}

public enum TransferDirection
{
    In = 1,
    Out = 2
}

public enum FetchedBy
{
    Doorbell = 1,
    Periodic = 2,
    Nightly = 3,
    Manual = 4
}

public enum ReviewReason
{
    NoCode = 1,
    UnknownCode = 2,
    AmbiguousCode = 3,
    VoidedReceipt = 4,
    TooLarge = 5
}

public enum IgnoreReason
{
    OtherAccount = 1,
    Duplicate = 2,
    NotTuition = 3,
    /// <summary>Received before the center switched to Auto: staff recorded it by hand (K21).</summary>
    BeforeAuto = 4
}

/// <summary>A transaction as the provider's API reported it, before it is stored.</summary>
public sealed record ReceivedBankTransaction(
    string ProviderTxnId,
    string BankCode,
    string AccountNumber,
    string? ProviderBankAccountId,
    string? VirtualAccount,
    TransferDirection Direction,
    long Amount,
    string Content,
    string? ReferenceCode,
    DateTimeOffset TransactedAtUtc,
    string RawPayload);

/// <summary>
/// One incoming transfer pulled from the provider API: the inbox row that every booking starts from.
/// Two keys stop duplicates: (provider, provider_txn_id) and the bank's own reference (semantic_key).
/// Money rows are never deleted.
/// </summary>
public sealed class BankTransaction : Entity<Guid>
{
    public const int MaxIdLength = 64;
    public const int MaxBankCodeLength = 64;
    public const int MaxAccountLength = 64;
    public const int MaxReferenceLength = 128;

    private BankTransaction() : base(Guid.Empty) { }

    private BankTransaction(Guid configId, Guid centerId, PaymentProvider provider, string? bankBin,
        ReceivedBankTransaction received, FetchedBy fetchedBy, DateTimeOffset now) : base(Guid.NewGuid())
    {
        ConfigId = configId;
        CenterId = centerId;
        Provider = provider;
        ProviderTxnId = Required(received.ProviderTxnId, MaxIdLength, nameof(received.ProviderTxnId));
        BankCode = Required(received.BankCode, MaxBankCodeLength, nameof(received.BankCode));
        BankBin = bankBin;
        AccountNumber = Required(received.AccountNumber, MaxAccountLength, nameof(received.AccountNumber));
        ProviderBankAccountId = Optional(received.ProviderBankAccountId, MaxIdLength);
        VirtualAccount = Optional(received.VirtualAccount, MaxAccountLength);
        ReferenceCode = Optional(received.ReferenceCode, MaxReferenceLength);
        Direction = received.Direction;
        Amount = received.Amount;
        Content = received.Content ?? string.Empty;
        TransactedAtUtc = received.TransactedAtUtc.ToUniversalTime();
        RawPayload = received.RawPayload;
        SemanticKey = BankTransactionKeys.SemanticKey(BankCode, AccountNumber, ReferenceCode);
        PayloadChecksum = BankTransactionKeys.Checksum(received);
        FetchedBy = fetchedBy;
        FirstSeenAtUtc = now;
        LastSeenAtUtc = now;
        SeenCount = 1;
        Status = BankTransactionStatus.Received;
    }

    public Guid ConfigId { get; private set; }
    public Guid CenterId { get; private set; }
    public PaymentProvider Provider { get; private set; }
    public string ProviderTxnId { get; private set; } = string.Empty;
    public string BankCode { get; private set; } = string.Empty;
    public string? BankBin { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public string? ProviderBankAccountId { get; private set; }
    public string? VirtualAccount { get; private set; }
    public string? ReferenceCode { get; private set; }
    public TransferDirection Direction { get; private set; }
    public long Amount { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public DateTimeOffset TransactedAtUtc { get; private set; }
    public string RawPayload { get; private set; } = "{}";

    /// <summary>bank|account|reference after normalizing; null when the bank gave no reference or the row is held.</summary>
    public string? SemanticKey { get; private set; }

    public Guid? DuplicateOfId { get; private set; }
    public string PayloadChecksum { get; private set; } = string.Empty;
    public FetchedBy FetchedBy { get; private set; }
    public DateTimeOffset FirstSeenAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public int SeenCount { get; private set; }
    public DateTimeOffset? BookedAtUtc { get; private set; }
    public BankTransactionStatus Status { get; private set; }
    public ReviewReason? ReviewReason { get; private set; }
    public IgnoreReason? IgnoreReason { get; private set; }
    public Guid? IntentId { get; private set; }
    public Guid? StudentId { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public Guid? ProcessingId { get; private set; }
    public DateTimeOffset? ProcessingUntilUtc { get; private set; }
    public string? LastError { get; private set; }
    public int Version { get; private set; }

    // What the matcher would have done while the center is in Shadow mode (filled from Round 3).
    public string? ShadowOutcome { get; private set; }
    public string? ShadowReason { get; private set; }
    public Guid? ShadowIntentId { get; private set; }
    public Guid? ShadowStudentId { get; private set; }
    public DateTimeOffset? ShadowEvaluatedAtUtc { get; private set; }

    /// <summary>An incoming transfer seen for the first time.</summary>
    public static BankTransaction Receive(PaymentProviderConfig config, ReceivedBankTransaction received, FetchedBy fetchedBy,
        DateTimeOffset now)
    {
        Validate(received, fetchedBy);
        var bankBin = SameAccount(config, received) ? config.BankBin : null;
        return new(config.Id, config.CenterId, config.Provider, bankBin, received, fetchedBy, now);
    }

    /// <summary>
    /// The provider reported a second id for a bank reference already stored. Kept for the owner to decide;
    /// it never books by itself and does not take the reference key.
    /// </summary>
    public static BankTransaction HoldAsDuplicate(PaymentProviderConfig config, ReceivedBankTransaction received,
        Guid duplicateOfId, FetchedBy fetchedBy, DateTimeOffset now)
    {
        if (duplicateOfId == Guid.Empty) throw new ArgumentException("The original row is required.", nameof(duplicateOfId));
        var row = Receive(config, received, fetchedBy, now);
        row.SemanticKey = null;
        row.DuplicateOfId = duplicateOfId;
        row.Status = BankTransactionStatus.Held;
        return row;
    }

    /// <summary>The bank feed booked it for this student.</summary>
    public void MarkBooked(Guid studentId, Guid? intentId, DateTimeOffset now)
    {
        RequireStatus(BankTransactionStatus.Received);
        Status = BankTransactionStatus.Booked;
        StudentId = studentId;
        IntentId = intentId;
        BookedAtUtc = now;
        ReviewReason = null;
        Finish();
    }

    /// <summary>A person has to say whose money this is.</summary>
    public void SendToReview(ReviewReason reason)
    {
        RequireStatus(BankTransactionStatus.Received);
        Status = BankTransactionStatus.NeedsReview;
        ReviewReason = reason;
        Finish();
    }

    /// <summary>Not tuition by rule (another account, before Auto); stays visible in reconciliation.</summary>
    public void IgnoreByRule(IgnoreReason reason)
    {
        RequireStatus(BankTransactionStatus.Received);
        Status = BankTransactionStatus.Ignored;
        IgnoreReason = reason;
        Finish();
    }

    /// <summary>Its receipt was voided: the money goes back to the queue (K16).</summary>
    public void ReturnAfterVoid()
    {
        if (Status != BankTransactionStatus.Booked) return;
        Status = BankTransactionStatus.NeedsReview;
        ReviewReason = Payments.ReviewReason.VoidedReceipt;
        StudentId = null;
        IntentId = null;
        BookedAtUtc = null;
        Version++;
    }

    private void RequireStatus(BankTransactionStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"The transaction is {Status}, not {expected}.");
    }

    private void Finish()
    {
        ProcessingId = null;
        ProcessingUntilUtc = null;
        LastError = null;
        Version++;
    }

    private static void Validate(ReceivedBankTransaction received, FetchedBy fetchedBy)
    {
        if (received.Direction != TransferDirection.In)
            throw new ArgumentException("Only incoming transfers are stored.", nameof(received));
        if (received.Amount <= 0) throw new ArgumentException("Amount must be positive.", nameof(received));
        if (!Enum.IsDefined(fetchedBy)) throw new ArgumentException("Unknown source.", nameof(fetchedBy));
        if (string.IsNullOrWhiteSpace(received.RawPayload)) throw new ArgumentException("Raw payload is required.", nameof(received));
    }

    private static bool SameAccount(PaymentProviderConfig config, ReceivedBankTransaction received) =>
        string.Equals(config.AccountNumber, received.AccountNumber?.Trim(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(config.AccountNumber, received.VirtualAccount?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Required(string? value, int max, string name)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) || text.Length > max ? throw new ArgumentException($"{name} is invalid.") : text;
    }

    private static string? Optional(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        return text.Length <= max ? text : throw new ArgumentException("Value is too long.");
    }
}

/// <summary>The normalized keys used to recognise the same transfer again.</summary>
public static class BankTransactionKeys
{
    /// <summary>bank|account|reference in upper case without spaces; null when the bank gave no reference.</summary>
    public static string? SemanticKey(string bankCode, string accountNumber, string? referenceCode)
    {
        var reference = Normalize(referenceCode);
        return reference.Length == 0 ? null : $"{Normalize(bankCode)}|{Normalize(accountNumber)}|{reference}";
    }

    /// <summary>
    /// Hash of the business fields only, so a new JSON field or another key order is not a change.
    /// Used to notice the provider changing a stored transaction, never as a duplicate key.
    /// </summary>
    public static string Checksum(ReceivedBankTransaction value)
    {
        var text = string.Join('\u001f',
            Normalize(value.AccountNumber),
            Normalize(value.VirtualAccount),
            value.Direction.ToString(),
            value.Amount.ToString(CultureInfo.InvariantCulture),
            (value.Content ?? string.Empty).Trim(),
            value.TransactedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Normalize(value.ReferenceCode));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(x => !char.IsWhiteSpace(x))).ToUpperInvariant();
}
