using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payments;

public enum ReconciliationTrigger
{
    Nightly = 1,
    Manual = 2
}

public enum ReconciliationStatus
{
    Completed = 1,
    /// <summary>The provider could not be read; the ledger checks still ran on what is stored.</summary>
    ScanFailed = 2
}

/// <summary>What a reconciliation found (docs/THANH-TOAN-QR-KE-HOACH.md, Đợt 5).</summary>
public static class FindingKinds
{
    /// <summary>The provider has a transfer that webhooks and the 5-minute pull both missed (it is stored now).</summary>
    public const string MissedByPull = "MissedByPull";
    /// <summary>K19: the provider changed a transfer after it was stored; the stored copy is kept.</summary>
    public const string ChangedAfterStore = "ChangedAfterStore";
    /// <summary>A correctly signed webhook with a bank reference, but the API has no such transfer after an hour.</summary>
    public const string DoorbellWithoutTransfer = "DoorbellWithoutTransfer";
    /// <summary>A Booked transfer without a live receipt.</summary>
    public const string BookedWithoutReceipt = "BookedWithoutReceipt";
    /// <summary>A live receipt that records a transfer which is not Booked, or an Auto receipt with no transfer.</summary>
    public const string ReceiptWithoutBookedTransfer = "ReceiptWithoutBookedTransfer";
    /// <summary>A live receipt whose amount differs from its transfer.</summary>
    public const string ReceiptAmountMismatch = "ReceiptAmountMismatch";
    /// <summary>A live receipt spread over charges for more than it is worth.</summary>
    public const string OverAllocated = "OverAllocated";
    /// <summary>K17: Σ Booked transfers ≠ Σ their live receipts.</summary>
    public const string TotalsMismatch = "TotalsMismatch";

    public const int MaxLength = 32;
}

/// <summary>One reconciliation of one connection. Insert only, kept forever (T13).</summary>
public sealed class ReconciliationRun : Entity<Guid>
{
    public const int MaxErrorLength = 500;

    private ReconciliationRun() : base(Guid.Empty) { }

    public ReconciliationRun(Guid centerId, Guid configId, ReconciliationTrigger trigger, DateTimeOffset windowFromUtc,
        DateTimeOffset windowToUtc, DateTimeOffset startedAtUtc) : base(Guid.NewGuid())
    {
        if (centerId == Guid.Empty || configId == Guid.Empty || windowFromUtc > windowToUtc)
            throw new ArgumentException("Reconciliation details are invalid.");
        CenterId = centerId;
        ConfigId = configId;
        Trigger = trigger;
        WindowFromUtc = windowFromUtc;
        WindowToUtc = windowToUtc;
        StartedAtUtc = startedAtUtc;
        FinishedAtUtc = startedAtUtc;
        Status = ReconciliationStatus.Completed;
    }

    public Guid CenterId { get; private set; }
    public Guid ConfigId { get; private set; }
    public ReconciliationTrigger Trigger { get; private set; }
    public DateTimeOffset WindowFromUtc { get; private set; }
    public DateTimeOffset WindowToUtc { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset FinishedAtUtc { get; private set; }
    public ReconciliationStatus Status { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Incoming transfers the provider listed for the window.</summary>
    public int ScannedCount { get; private set; }
    public int MissedCount { get; private set; }
    public int ChangedCount { get; private set; }

    /// <summary>K17 for the window: every incoming đồng is in exactly one of these buckets.</summary>
    public long IncomingTotal { get; private set; }
    public long BookedTotal { get; private set; }
    public long BookedReceiptTotal { get; private set; }
    public long PendingTotal { get; private set; }
    public long FailedTotal { get; private set; }
    public long IgnoredTotal { get; private set; }

    public int FindingCount { get; private set; }

    public void ScanFailed(string error)
    {
        Status = ReconciliationStatus.ScanFailed;
        Error = error.Length <= MaxErrorLength ? error : error[..MaxErrorLength];
    }

    public void Scanned(int scanned, int missed, int changed)
    {
        ScannedCount = scanned;
        MissedCount = missed;
        ChangedCount = changed;
    }

    public void Totals(long incoming, long booked, long bookedReceipts, long pending, long failed, long ignored)
    {
        IncomingTotal = incoming;
        BookedTotal = booked;
        BookedReceiptTotal = bookedReceipts;
        PendingTotal = pending;
        FailedTotal = failed;
        IgnoredTotal = ignored;
    }

    public void Finish(int findings, DateTimeOffset now)
    {
        FindingCount = findings;
        FinishedAtUtc = now;
    }
}

public sealed class ReconciliationFinding : Entity<Guid>
{
    public const int MaxDetailLength = 500;

    private ReconciliationFinding() : base(Guid.Empty) { }

    public ReconciliationFinding(Guid runId, Guid centerId, string kind, Guid? bankTransactionId, Guid? receiptId,
        Guid? deliveryId, long? amount, string detail) : base(Guid.NewGuid())
    {
        if (runId == Guid.Empty || centerId == Guid.Empty || string.IsNullOrWhiteSpace(kind) || kind.Length > FindingKinds.MaxLength)
            throw new ArgumentException("Finding details are invalid.");
        RunId = runId;
        CenterId = centerId;
        Kind = kind;
        BankTransactionId = bankTransactionId;
        ReceiptId = receiptId;
        DeliveryId = deliveryId;
        Amount = amount;
        Detail = detail.Length <= MaxDetailLength ? detail : detail[..MaxDetailLength];
    }

    public Guid RunId { get; private set; }
    public Guid CenterId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public Guid? BankTransactionId { get; private set; }
    public Guid? ReceiptId { get; private set; }
    public Guid? DeliveryId { get; private set; }
    public long? Amount { get; private set; }
    public string Detail { get; private set; } = string.Empty;
}

public enum PaymentAlertState
{
    Open = 1,
    /// <summary>The condition went away by itself; it opens again (and emails again) if it comes back.</summary>
    Resolved = 2,
    /// <summary>An owner looked at it; it stays quiet even if seen again.</summary>
    Acknowledged = 3
}

/// <summary>D10 alerts. One row per center and fingerprint, so a condition seen again never emails twice.</summary>
public static class AlertKinds
{
    public const string TransferFailed = "TransferFailed";
    public const string ApiDown = "ApiDown";
    public const string BadSignatures = "BadSignatures";
    public const string StuckReceived = "StuckReceived";
    public const string ReceiptConflict = "ReceiptConflict";
    /// <summary>Changed or unknown data from the provider, and transfers only the reconciliation caught: an owner acknowledges.</summary>
    public const string ProviderData = "ProviderData";
    /// <summary>A ledger inconsistency; closes when the next reconciliation no longer finds it.</summary>
    public const string Reconciliation = "Reconciliation";

    public const int MaxLength = 32;
}

/// <summary>An owner alert. Written with plain SQL (insert-or-reopen is atomic); mapped for reads and the schema.</summary>
public sealed class PaymentAlert : Entity<Guid>
{
    public const int MaxFingerprintLength = 128;
    public const int MaxTitleLength = 200;
    public const int MaxDetailLength = 1000;

    private PaymentAlert() : base(Guid.Empty) { }

    public Guid CenterId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Detail { get; private set; } = string.Empty;
    public PaymentAlertState State { get; private set; }
    /// <summary>A number the rule compares against next time (for example the bad-signature count when raised).</summary>
    public long? Metric { get; private set; }
    public DateTimeOffset FirstSeenAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }
    public DateTimeOffset? LastEmailedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }
}
