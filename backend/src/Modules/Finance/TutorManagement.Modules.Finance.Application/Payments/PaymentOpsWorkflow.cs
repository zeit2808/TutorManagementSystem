using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payments;

public sealed class ReconciliationRunDto
{
    public Guid Id { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Error { get; set; }
    public DateTimeOffset WindowFromUtc { get; set; }
    public DateTimeOffset WindowToUtc { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset FinishedAtUtc { get; set; }
    public int ScannedCount { get; set; }
    public int MissedCount { get; set; }
    public int ChangedCount { get; set; }
    public long IncomingTotal { get; set; }
    public long BookedTotal { get; set; }
    public long BookedReceiptTotal { get; set; }
    public long PendingTotal { get; set; }
    public long FailedTotal { get; set; }
    public long IgnoredTotal { get; set; }
    /// <summary>K17 for the window: every incoming đồng is in one bucket, and Booked equals its live receipts.</summary>
    public bool TotalsAgree { get; set; }
    public int FindingCount { get; set; }
    public List<ReconciliationFindingDto> Findings { get; set; } = [];
}

public sealed class ReconciliationFindingDto
{
    public string Kind { get; set; } = string.Empty;
    public Guid? BankTransactionId { get; set; }
    public Guid? ReceiptId { get; set; }
    public Guid? DeliveryId { get; set; }
    public long? Amount { get; set; }
    public string Detail { get; set; } = string.Empty;
}

public sealed class PaymentAlertDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public DateTimeOffset? AcknowledgedAtUtc { get; set; }
}

/// <summary>Đợt 5 metrics for one center over the last <c>Days</c> days, read from what is already stored.</summary>
public sealed class PaymentMetricsDto
{
    public int Days { get; set; }
    public DateTimeOffset FromUtc { get; set; }
    public DateTimeOffset ToUtc { get; set; }
    /// <summary>Correctly signed webhooks first seen in the window, and how many SePay sent more than once.</summary>
    public int Doorbells { get; set; }
    public int DoorbellsRedelivered { get; set; }
    /// <summary>Doorbells per hour for the last 24 hours, oldest first (24 values).</summary>
    public List<int> DoorbellsByHour { get; set; } = [];
    public long InvalidSignatureTotal { get; set; }
    public DateTimeOffset? LastInvalidSignatureAtUtc { get; set; }
    /// <summary>Incoming transfers stored in the window, by status and by what found them first.</summary>
    public int Transfers { get; set; }
    public long TransferAmount { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = [];
    public Dictionary<string, int> ByFetchedBy { get; set; } = [];
    /// <summary>Transfers the provider listed again after they were stored (absorbed by the keys).</summary>
    public int SeenAgain { get; set; }
    /// <summary>Attempts to book a second receipt that the database stopped. Should always be 0.</summary>
    public int ReceiptConflicts { get; set; }
    public int ManualAssigned { get; set; }
    public int ManualIgnored { get; set; }
    public int ManualRetried { get; set; }
    /// <summary>Seconds from the bank transfer to its Auto receipt.</summary>
    public double? LatencyP50Seconds { get; set; }
    public double? LatencyP95Seconds { get; set; }
    public double? LatencyMaxSeconds { get; set; }
    /// <summary>How long the oldest transfer still waiting to be booked has waited, in seconds.</summary>
    public double? OldestReceivedSeconds { get; set; }
    public int OpenAlerts { get; set; }
    public DateTimeOffset? LastReconciledAtUtc { get; set; }
    public string? LastReconciliationStatus { get; set; }
    public int? LastReconciliationFindings { get; set; }
}

public interface IPaymentOpsReader
{
    Task<PaymentMetricsDto> MetricsAsync(Guid centerId, DateTimeOffset now, int days, CancellationToken token);

    Task<(IReadOnlyList<ReconciliationRun> Items, int Total)> ListRunsAsync(Guid centerId, int page, int pageSize,
        CancellationToken token);

    Task<(ReconciliationRun Run, IReadOnlyList<ReconciliationFinding> Findings)?> FindRunAsync(Guid centerId, Guid runId,
        CancellationToken token);

    Task<IReadOnlyList<PaymentAlert>> ListAlertsAsync(Guid centerId, PaymentAlertState? state, int max, CancellationToken token);

    Task<PaymentAlert?> FindAlertAsync(Guid centerId, Guid alertId, CancellationToken token);

    /// <summary>Open → Acknowledged, with an audit row; false if it is not open any more.</summary>
    Task<bool> AcknowledgeAlertAsync(Guid centerId, Guid alertId, Guid actorId, DateTimeOffset now, CancellationToken token);
}

public static class PaymentOpsErrors
{
    public static readonly Error RunNotFound = new("payment_reconciliation_not_found", "The reconciliation was not found.");
    public static readonly Error AlertNotFound = new("payment_alert_not_found", "The alert was not found.");
    public static readonly Error AlertNotOpen = new("payment_alert_not_open", "The alert is not open any more.");
    public static readonly Error InvalidDays = new("validation_error", "days must be 1–31.", "days");
    public static readonly Error InvalidState = new("validation_error", "state must be Open, Acknowledged, Resolved or All.", "state");
}

/// <summary>Reconciliation history, alerts and "reconcile now" for the center's owner and managers (Đợt 5).</summary>
public sealed class PaymentOpsWorkflow(IPaymentOpsReader reader, IPaymentOpsStore ops, IClock clock)
{
    public async Task<TuitionPage<ReconciliationRunDto>> ListRunsAsync(Guid centerId, int page, int pageSize, CancellationToken token)
    {
        var (items, total) = await reader.ListRunsAsync(centerId, page, pageSize, token);
        return new TuitionPage<ReconciliationRunDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total, Items = [.. items.Select(x => Run(x, []))]
        };
    }

    public async Task<Result<ReconciliationRunDto>> GetRunAsync(Guid centerId, Guid runId, CancellationToken token) =>
        await reader.FindRunAsync(centerId, runId, token) is { } found
            ? Result<ReconciliationRunDto>.Success(Run(found.Run, found.Findings))
            : Result<ReconciliationRunDto>.Failure(PaymentOpsErrors.RunNotFound);

    /// <summary>The Worker starts it within half a minute. Fails when the center has no connection that is on.</summary>
    public async Task<Result<bool>> RequestAsync(Guid centerId, CancellationToken token) =>
        await ops.RequestReconciliationAsync(centerId, clock.UtcNow, token)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(PaymentErrors.NotPulling);

    public async Task<Result<PaymentMetricsDto>> MetricsAsync(Guid centerId, int days, CancellationToken token) =>
        days is < 1 or > 31
            ? Result<PaymentMetricsDto>.Failure(PaymentOpsErrors.InvalidDays)
            : Result<PaymentMetricsDto>.Success(await reader.MetricsAsync(centerId, clock.UtcNow, days, token));

    public async Task<Result<List<PaymentAlertDto>>> ListAlertsAsync(Guid centerId, string? state, CancellationToken token)
    {
        PaymentAlertState? wanted = null;
        if (!string.IsNullOrEmpty(state) && !string.Equals(state, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<PaymentAlertState>(state, true, out var parsed) || !Enum.IsDefined(parsed) ||
                int.TryParse(state, out _))
                return Result<List<PaymentAlertDto>>.Failure(PaymentOpsErrors.InvalidState);
            wanted = parsed;
        }
        var alerts = await reader.ListAlertsAsync(centerId, wanted, 200, token);
        return Result<List<PaymentAlertDto>>.Success([.. alerts.Select(Alert)]);
    }

    public async Task<Result<PaymentAlertDto>> AcknowledgeAsync(Guid centerId, Guid alertId, Guid actorId, CancellationToken token)
    {
        if (await reader.FindAlertAsync(centerId, alertId, token) is null)
            return Result<PaymentAlertDto>.Failure(PaymentOpsErrors.AlertNotFound);
        if (!await reader.AcknowledgeAlertAsync(centerId, alertId, actorId, clock.UtcNow, token))
            return Result<PaymentAlertDto>.Failure(PaymentOpsErrors.AlertNotOpen);
        return Result<PaymentAlertDto>.Success(Alert((await reader.FindAlertAsync(centerId, alertId, token))!));
    }

    private static ReconciliationRunDto Run(ReconciliationRun run, IReadOnlyList<ReconciliationFinding> findings) => new()
    {
        Id = run.Id, Trigger = run.Trigger.ToString(), Status = run.Status.ToString(), Error = run.Error,
        WindowFromUtc = run.WindowFromUtc, WindowToUtc = run.WindowToUtc, StartedAtUtc = run.StartedAtUtc,
        FinishedAtUtc = run.FinishedAtUtc, ScannedCount = run.ScannedCount, MissedCount = run.MissedCount,
        ChangedCount = run.ChangedCount, IncomingTotal = run.IncomingTotal, BookedTotal = run.BookedTotal,
        BookedReceiptTotal = run.BookedReceiptTotal, PendingTotal = run.PendingTotal, FailedTotal = run.FailedTotal,
        IgnoredTotal = run.IgnoredTotal, FindingCount = run.FindingCount,
        TotalsAgree = run.BookedTotal == run.BookedReceiptTotal &&
                      run.IncomingTotal == run.BookedTotal + run.PendingTotal + run.FailedTotal + run.IgnoredTotal,
        Findings = [.. findings.Select(x => new ReconciliationFindingDto
        {
            Kind = x.Kind, BankTransactionId = x.BankTransactionId, ReceiptId = x.ReceiptId, DeliveryId = x.DeliveryId,
            Amount = x.Amount, Detail = x.Detail
        })]
    };

    private static PaymentAlertDto Alert(PaymentAlert x) => new()
    {
        Id = x.Id, Kind = x.Kind, Title = x.Title, Detail = x.Detail, State = x.State.ToString(),
        FirstSeenAtUtc = x.FirstSeenAtUtc, LastSeenAtUtc = x.LastSeenAtUtc, ResolvedAtUtc = x.ResolvedAtUtc,
        AcknowledgedAtUtc = x.AcknowledgedAtUtc
    };
}
