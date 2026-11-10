using System.Security.Cryptography;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payments;

public enum PaymentProvider
{
    Sepay = 1
}

/// <summary>Off: nothing is pulled. Shadow: transactions are pulled and matched on paper only. Auto: matches become receipts.</summary>
public enum PaymentProviderMode
{
    Off = 1,
    Shadow = 2,
    Auto = 3
}

/// <summary>
/// One center's connection to its bank feed (docs/THANH-TOAN-QR-KE-HOACH.md, section 4).
/// Secrets are stored encrypted; the plain values never leave the server.
/// The pull schedule lives here so a restarted Worker keeps its rhythm.
/// </summary>
public sealed class PaymentProviderConfig : Entity<Guid>
{
    public const int EndpointKeyLength = 43;
    public const int MaxAccountNumberLength = 30;

    private PaymentProviderConfig() : base(Guid.Empty) { }

    private PaymentProviderConfig(Guid centerId, PaymentProvider provider, string bankBin, string accountNumber,
        ProtectedSecrets secrets, Guid actorId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Provider = provider;
        EndpointKey = NewEndpointKey();
        BankBin = bankBin;
        AccountNumber = accountNumber;
        Mode = PaymentProviderMode.Off;
        CreatedByUserId = actorId;
        CreatedAtUtc = now;
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now;
        Apply(secrets);
    }

    public Guid CenterId { get; private set; }
    public PaymentProvider Provider { get; private set; }

    /// <summary>Routes webhooks to this row. Not a secret: the webhook secret is the HMAC key.</summary>
    public string EndpointKey { get; private set; } = string.Empty;

    public byte[] WebhookSecret { get; private set; } = [];
    public byte[] ApiToken { get; private set; } = [];

    /// <summary>Keyed hash of the API token: two configs can never share one token.</summary>
    public string ApiTokenFingerprint { get; private set; } = string.Empty;

    /// <summary>Which encryption key protects <see cref="WebhookSecret"/> and <see cref="ApiToken"/>.</summary>
    public string KeyId { get; private set; } = string.Empty;

    public string BankBin { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public PaymentProviderMode Mode { get; private set; }

    /// <summary>When Auto was switched on: transactions before this were recorded by hand and never book themselves.</summary>
    public DateTimeOffset? AutoSinceUtc { get; private set; }

    public DateTimeOffset? PullRequestedAtUtc { get; private set; }
    public DateTimeOffset? NextPullAtUtc { get; private set; }
    public int PullRetryCount { get; private set; }
    public DateTimeOffset? LastPulledAtUtc { get; private set; }
    public string? PullCursor { get; private set; }
    public Guid? PullProcessingId { get; private set; }
    public DateTimeOffset? PullProcessingUntilUtc { get; private set; }
    public string? LastPullError { get; private set; }
    public long InvalidSignatureCount { get; private set; }
    public DateTimeOffset? LastInvalidSignatureAtUtc { get; private set; }

    // Đợt 5, written by the Worker with plain SQL under a lease: the nightly reconciliation and the morning digest.
    public DateTimeOffset? NextReconcileAtUtc { get; private set; }
    public Guid? ReconcileProcessingId { get; private set; }
    public DateTimeOffset? ReconcileProcessingUntilUtc { get; private set; }
    public DateTimeOffset? ReconcileRequestedAtUtc { get; private set; }
    public DateOnly? DigestSentOn { get; private set; }
    public int Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static PaymentProviderConfig Create(Guid centerId, PaymentProvider provider, string bankBin, string accountNumber,
        ProtectedSecrets secrets, Guid actorId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || actorId == Guid.Empty || !Enum.IsDefined(provider))
            throw new ArgumentException("Payment provider details are invalid.");
        return new(centerId, provider, Bin(bankBin), Account(accountNumber), secrets, actorId, now);
    }

    public void ReplaceSecrets(ProtectedSecrets secrets, Guid actorId, DateTimeOffset now)
    {
        Apply(secrets);
        Touch(actorId, now);
    }

    public void ChangeAccount(string bankBin, string accountNumber, Guid actorId, DateTimeOffset now)
    {
        BankBin = Bin(bankBin);
        AccountNumber = Account(accountNumber);
        Touch(actorId, now);
    }

    public void ChangeMode(PaymentProviderMode mode, Guid actorId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown mode.", nameof(mode));
        if (mode == Mode) return;
        if (mode == PaymentProviderMode.Auto) AutoSinceUtc = now;
        if (Mode == PaymentProviderMode.Off) NextPullAtUtc = now;
        Mode = mode;
        Touch(actorId, now);
    }

    /// <summary>A correctly signed webhook arrived: pull as soon as the Worker is free.</summary>
    public void RequestPull(DateTimeOffset now)
    {
        if (Mode == PaymentProviderMode.Off) return;
        PullRequestedAtUtc = now;
        NextPullAtUtc = now;
    }

    public void RecordInvalidSignature(DateTimeOffset now)
    {
        InvalidSignatureCount++;
        LastInvalidSignatureAtUtc = now;
    }

    public static string NewEndpointKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private void Apply(ProtectedSecrets secrets)
    {
        if (secrets.WebhookSecret.Length == 0 || secrets.ApiToken.Length == 0 ||
            string.IsNullOrWhiteSpace(secrets.ApiTokenFingerprint) || string.IsNullOrWhiteSpace(secrets.KeyId))
            throw new ArgumentException("Protected secrets are incomplete.", nameof(secrets));
        WebhookSecret = secrets.WebhookSecret;
        ApiToken = secrets.ApiToken;
        ApiTokenFingerprint = secrets.ApiTokenFingerprint;
        KeyId = secrets.KeyId;
    }

    private void Touch(Guid actorId, DateTimeOffset now)
    {
        if (actorId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorId));
        UpdatedByUserId = actorId;
        UpdatedAtUtc = now;
        Version++;
    }

    private static string Bin(string value) =>
        value is { Length: 6 } && value.All(char.IsAsciiDigit) ? value : throw new ArgumentException("Bank BIN must be 6 digits.");

    private static string Account(string value)
    {
        var account = value?.Trim() ?? string.Empty;
        return account.Length is > 0 and <= MaxAccountNumberLength && account.All(char.IsAsciiLetterOrDigit)
            ? account
            : throw new ArgumentException("Account number is invalid.");
    }
}

/// <summary>The encrypted secrets of one config and the key that protects them.</summary>
public sealed record ProtectedSecrets(byte[] WebhookSecret, byte[] ApiToken, string ApiTokenFingerprint, string KeyId);
