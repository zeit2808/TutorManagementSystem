using System.Globalization;
using System.Text;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>A connection the Worker leased for one reconciliation.</summary>
public sealed record ReconcileTarget(Guid ConfigId, Guid CenterId, PaymentProvider Provider, byte[] ApiToken, string KeyId,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? RequestedAtUtc);

/// <summary>Bank transactions a scan stored for the first time, and stored ones the provider now reports differently.</summary>
public sealed record StoredScan(IReadOnlyList<Guid> Inserted, IReadOnlyList<Guid> Changed);

/// <summary>K17 for one window: Σ incoming = booked + pending + failed + ignored, and booked = Σ its live receipts.</summary>
public sealed record LedgerTotals(long Incoming, long Booked, long BookedReceipts, long Pending, long Failed, long Ignored);

public sealed record FindingDraft(string Kind, Guid? BankTransactionId, Guid? ReceiptId, Guid? DeliveryId, long? Amount,
    string Detail)
{
    public string Fingerprint => $"{Kind}:{BankTransactionId ?? ReceiptId ?? DeliveryId}";
}

/// <summary>A condition to alert about. Same fingerprint, same alert: it is emailed once when it opens.</summary>
public sealed record AlertDraft(string Fingerprint, string Kind, string Title, string Detail, long? Metric = null);

public sealed record RaisedAlert(Guid Id, string Kind, string Title, string Detail);

/// <summary>An open alert for the morning digest.</summary>
public sealed record OpenAlert(Guid Id, string Kind, string Title, string Detail, DateTimeOffset FirstSeenAtUtc);

/// <summary>What the minute monitor looks at, for every center with a connection or an open alert.</summary>
public sealed record MonitorSnapshot(
    IReadOnlyList<MonitorConfig> Configs,
    IReadOnlyList<MonitorTransfer> Failed,
    IReadOnlyList<MonitorTransfer> Conflicts,
    IReadOnlyList<MonitorStuck> Stuck,
    IReadOnlyList<MonitorProviderEvent> ProviderEvents,
    IReadOnlyList<Guid> CentersWithOpenAlerts);

public sealed record MonitorConfig(Guid ConfigId, Guid CenterId, PaymentProviderMode Mode, string? LastPullError,
    DateTimeOffset? LastPulledAtUtc, DateTimeOffset CreatedAtUtc, long InvalidSignatureCount,
    DateTimeOffset? LastInvalidSignatureAtUtc, long? SignatureBaseline, bool SignatureAlertOpen);

public sealed record MonitorTransfer(Guid Id, Guid CenterId, long Amount, string Content, DateTimeOffset TransactedAtUtc,
    string? LastError);

public sealed record MonitorStuck(Guid ConfigId, Guid CenterId, int Count, long Amount, DateTimeOffset OldestSeenAtUtc);

/// <summary>Provider data that needs a person: a stored transfer changed (K19) or a field we do not know appeared.</summary>
public sealed record MonitorProviderEvent(Guid CenterId, string Action, Guid? TargetId, string Detail);

/// <summary>A center due its morning digest, claimed atomically inside the caller's transaction.</summary>
public sealed record DigestBacklog(int NeedsReview, int Held, int Failed, long Amount);

public interface IPaymentOpsStore
{
    /// <summary>
    /// Gives new connections their first nightly slot, then leases up to <paramref name="max"/> connections whose
    /// reconciliation is due (FOR UPDATE SKIP LOCKED).
    /// </summary>
    /// <param name="onlyConfigId">Limits the claim to one connection (tests; a shared database has other centers).</param>
    Task<IReadOnlyList<ReconcileTarget>> ClaimDueReconciliationsAsync(Guid leaseId, DateTimeOffset now,
        DateTimeOffset firstNightly, TimeSpan lease, int max, Guid? onlyConfigId, CancellationToken token);

    /// <summary>Stores scanned transfers through the same two keys as a pull, without touching the pull cursor.</summary>
    Task<StoredScan> StoreScanAsync(Guid configId, IReadOnlyList<ReceivedBankTransaction> incoming, FetchedBy fetchedBy,
        DateTimeOffset now, CancellationToken token);

    /// <summary>Whole-history checks of the center's stored state: these do not need the provider.</summary>
    Task<IReadOnlyList<FindingDraft>> CheckLedgerAsync(Guid centerId, DateTimeOffset now, CancellationToken token);

    Task<LedgerTotals> TotalsAsync(Guid configId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token);

    /// <summary>One transaction: the run, its findings and the next slot. False, writing nothing, if the lease was lost.</summary>
    Task<bool> FinishReconciliationAsync(ReconcileTarget target, Guid leaseId, ReconciliationRun run,
        IReadOnlyList<ReconciliationFinding> findings, DateTimeOffset nextNightly, CancellationToken token);

    /// <summary>An owner asked for a reconciliation now. False if the center has no connection that is on.</summary>
    Task<bool> RequestReconciliationAsync(Guid centerId, DateTimeOffset now, CancellationToken token);

    /// <summary>Opens a transaction holding the center's alert lock, on the same connection as the tuition store.</summary>
    Task<ITuitionGuard> BeginAlertScopeAsync(Guid centerId, CancellationToken token);

    /// <summary>
    /// Inside <see cref="BeginAlertScopeAsync"/>: inserts new fingerprints, reopens resolved ones, touches open ones,
    /// and resolves open or acknowledged alerts of <paramref name="resolvableKinds"/> that are not in the drafts.
    /// Returns the alerts that opened now (their last-emailed time is set).
    /// </summary>
    Task<IReadOnlyList<RaisedAlert>> ApplyAlertsAsync(Guid centerId, IReadOnlyList<AlertDraft> drafts,
        IReadOnlyCollection<string> resolvableKinds, DateTimeOffset now, CancellationToken token);

    Task<MonitorSnapshot> ReadMonitorSnapshotAsync(DateTimeOffset now, CancellationToken token);

    /// <summary>Centers with a connection that is on whose digest for <paramref name="today"/> was not sent yet.</summary>
    Task<IReadOnlyList<Guid>> ListDigestDueAsync(DateOnly today, CancellationToken token);

    /// <summary>
    /// Inside <see cref="BeginAlertScopeAsync"/>: marks today's digest as sent (false if another Worker did) and returns
    /// what it should say: open alerts and queue items older than a day.
    /// </summary>
    Task<(bool Claimed, IReadOnlyList<OpenAlert> Alerts, DigestBacklog Queue)> ClaimDigestAsync(Guid centerId, DateOnly today,
        DateTimeOffset now, CancellationToken token);
}

/// <summary>
/// D10 alerts (docs/THANH-TOAN-QR-KE-HOACH.md): one email to every owner when an alert opens, never twice for the
/// same fingerprint while it stays open; the morning digest repeats what is still open.
/// </summary>
public sealed class PaymentAlertBook(IPaymentOpsStore ops, ITuitionStore tuition, ICenterMemberDirectory members, IClock clock)
{
    public async Task<IReadOnlyList<RaisedAlert>> ApplyAsync(Guid centerId, IReadOnlyList<AlertDraft> drafts,
        IReadOnlyCollection<string> resolvableKinds, CancellationToken token)
    {
        await using var scope = await ops.BeginAlertScopeAsync(centerId, token);
        var now = clock.UtcNow;
        var raised = await ops.ApplyAlertsAsync(centerId, drafts, resolvableKinds, now, token);
        if (raised.Count > 0)
        {
            var subject = raised.Count == 1 ? $"Cảnh báo thanh toán: {raised[0].Title}" : $"Cảnh báo thanh toán: {raised.Count} vấn đề mới";
            var body = new StringBuilder("Hệ thống thanh toán tự động vừa phát hiện:\n\n");
            foreach (var alert in raised) body.Append("- ").Append(alert.Title).Append('\n').Append("  ").Append(alert.Detail).Append('\n');
            body.Append('\n').Append(Footer(now));
            await EmailOwnersAsync(centerId, subject, body.ToString(), token);
        }
        await scope.CommitAsync(token);
        return raised;
    }

    /// <summary>Sends the morning digest of one center if it is due and has something to say.</summary>
    public async Task<bool> SendDigestAsync(Guid centerId, DateOnly today, CancellationToken token)
    {
        await using var scope = await ops.BeginAlertScopeAsync(centerId, token);
        var now = clock.UtcNow;
        var (claimed, alerts, queue) = await ops.ClaimDigestAsync(centerId, today, now, token);
        var sent = false;
        if (claimed && (alerts.Count > 0 || queue.NeedsReview + queue.Held + queue.Failed > 0))
        {
            var body = new StringBuilder("Tổng hợp thanh toán tự động sáng nay:\n\n");
            if (queue.NeedsReview + queue.Held + queue.Failed > 0)
            {
                body.Append(CultureInfo.InvariantCulture,
                    $"Hàng chờ có việc tồn quá 24 giờ ({Money(queue.Amount)}): {queue.NeedsReview} khoản chưa rõ của ai, ")
                    .Append(CultureInfo.InvariantCulture, $"{queue.Held} khoản nghi trùng, {queue.Failed} khoản lỗi xử lý.\n")
                    .Append("Xử lý ở Học phí → Chuyển khoản.\n\n");
            }
            if (alerts.Count > 0)
            {
                body.Append("Cảnh báo còn mở:\n");
                foreach (var alert in alerts)
                    body.Append("- ").Append(alert.Title).Append(" (từ ").Append(Local(alert.FirstSeenAtUtc)).Append(")\n  ")
                        .Append(alert.Detail).Append('\n');
                body.Append('\n');
            }
            body.Append(Footer(now));
            await EmailOwnersAsync(centerId, "Tổng hợp thanh toán tự động", body.ToString(), token);
            sent = true;
        }
        await scope.CommitAsync(token);
        return sent;
    }

    private async Task EmailOwnersAsync(Guid centerId, string subject, string body, CancellationToken token)
    {
        var owners = await members.ListActiveUserIdsAsync(centerId, ["CenterOwner"], token);
        if (owners.Count == 0) return;
        var emails = await tuition.ReadEmailsAsync(owners, token);
        await tuition.QueueEmailsAsync(owners.Where(emails.ContainsKey)
            .Select(x => new OutgoingTuitionEmail(emails[x], subject, body)).ToArray(), token);
    }

    private static string Footer(DateTimeOffset now) =>
        $"Thời điểm: {Local(now)} (giờ Việt Nam). Xem và đánh dấu đã xem ở Học phí → Thanh toán tự động.";

    internal static string Local(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, FinanceTime.Zone).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    internal static string Money(long amount) => amount.ToString("#,0", CultureInfo.GetCultureInfo("vi-VN")) + "đ";
}

/// <summary>
/// Đợt 5: every night at 02:00 (Vietnam) each connection re-reads the last 3 days from the provider through the same
/// keys as a pull, then checks the stored ledger: Booked ↔ live receipt ↔ allocations, webhooks ↔ API transfers, and the
/// K17 totals. Results are kept forever; anything wrong becomes an owner alert. Only a check: money is still booked by
/// the normal path.
/// </summary>
public sealed class PaymentReconciler(IPaymentOpsStore ops, IEnumerable<IBankFeedProvider> providers, ISecretProtector protector,
    PaymentAlertBook alerts, IClock clock)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(3);
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(15);
    public static readonly TimeOnly NightlyAt = new(2, 0);

    /// <summary>Slower than a pull, so a pull running at the same time still fits SePay's 3 requests per second.</summary>
    public static readonly TimeSpan PageSpacing = TimeSpan.FromMilliseconds(700);

    private const int MaxRateLimitRetries = 5;

    private readonly Dictionary<PaymentProvider, IBankFeedProvider> _providers = providers.ToDictionary(x => x.Provider);

    /// <summary>The first 02:00 in Vietnam strictly after <paramref name="now"/>.</summary>
    public static DateTimeOffset NextNightly(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, FinanceTime.Zone);
        var at = new DateTimeOffset(local.Date.Add(NightlyAt.ToTimeSpan()), local.Offset);
        if (at <= local) at = at.AddDays(1);
        return at.ToUniversalTime();
    }

    public async Task<int> RunOnceAsync(int max, CancellationToken token, Guid? onlyConfigId = null)
    {
        var leaseId = Guid.NewGuid();
        var now = clock.UtcNow;
        var targets = await ops.ClaimDueReconciliationsAsync(leaseId, now, NextNightly(now), Lease, max, onlyConfigId, token);
        foreach (var target in targets) await ReconcileAsync(target, leaseId, token);
        return targets.Count;
    }

    private async Task ReconcileAsync(ReconcileTarget target, Guid leaseId, CancellationToken token)
    {
        var started = clock.UtcNow;
        var from = target.CreatedAtUtc > started - Window ? target.CreatedAtUtc : started - Window;
        var run = new ReconciliationRun(target.CenterId, target.ConfigId,
            target.RequestedAtUtc is null ? ReconciliationTrigger.Nightly : ReconciliationTrigger.Manual, from, started, started);
        var findings = new List<FindingDraft>();

        try
        {
            var incoming = await ScanAsync(target, from, token);
            var stored = await ops.StoreScanAsync(target.ConfigId, incoming,
                run.Trigger == ReconciliationTrigger.Manual ? FetchedBy.Manual : FetchedBy.Nightly, clock.UtcNow, token);
            run.Scanned(incoming.Count, stored.Inserted.Count, stored.Changed.Count);
            findings.AddRange(stored.Inserted.Select(id => new FindingDraft(FindingKinds.MissedByPull, id, null, null, null,
                "SePay có giao dịch này nhưng webhook và lần kéo 5 phút đều bỏ lỡ; đối soát đã lưu và xử lý như bình thường.")));
            findings.AddRange(stored.Changed.Select(id => new FindingDraft(FindingKinds.ChangedAfterStore, id, null, null, null,
                "SePay báo dữ liệu khác với bản đã lưu; hệ thống giữ bản đã ghi, hãy đối chiếu sao kê ngân hàng.")));
        }
        catch (BankFeedException error) { run.ScanFailed(error.Message); }
        catch (System.Security.Cryptography.CryptographicException)
        { run.ScanFailed("The stored API token cannot be decrypted with the configured keys."); }

        // These read only what is stored, so they run even when SePay could not be reached.
        var ledger = await ops.CheckLedgerAsync(target.CenterId, clock.UtcNow, token);
        findings.AddRange(ledger);
        var totals = await ops.TotalsAsync(target.ConfigId, from, started, token);
        run.Totals(totals.Incoming, totals.Booked, totals.BookedReceipts, totals.Pending, totals.Failed, totals.Ignored);
        run.Finish(findings.Count, clock.UtcNow);

        var rows = findings.Select(x => new ReconciliationFinding(run.Id, target.CenterId, x.Kind, x.BankTransactionId,
            x.ReceiptId, x.DeliveryId, x.Amount, x.Detail)).ToArray();
        if (!await ops.FinishReconciliationAsync(target, leaseId, run, rows, NextNightly(clock.UtcNow), token)) return;

        var drafts = findings.Select(x => new AlertDraft(x.Fingerprint,
            x.Kind is FindingKinds.MissedByPull or FindingKinds.ChangedAfterStore ? AlertKinds.ProviderData : AlertKinds.Reconciliation,
            Title(x.Kind, x.Amount), x.Detail)).ToList();
        await alerts.ApplyAsync(target.CenterId, drafts, [AlertKinds.Reconciliation], token);
    }

    private async Task<List<ReceivedBankTransaction>> ScanAsync(ReconcileTarget target, DateTimeOffset from, CancellationToken token)
    {
        if (!_providers.TryGetValue(target.Provider, out var provider))
            throw new BankFeedException($"No feed for {target.Provider}.", false);
        var apiToken = protector.Unprotect(target.ApiToken, target.KeyId,
            SecretPurposes.ApiToken(target.CenterId, target.Provider.ToString()));
        var received = new List<ReceivedBankTransaction>();
        for (var page = 1; page <= PaymentFetcher.MaxPages; page++)
        {
            if (page > 1) await Task.Delay(PageSpacing, token);
            BankFeedPage result;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    result = await provider.ListAsync(apiToken, new BankFeedQuery(null, from, page, PaymentFetcher.PerPage), token);
                    break;
                }
                catch (BankFeedException error) when (error.RetryAfter is { } wait && attempt < MaxRateLimitRetries)
                {
                    await Task.Delay(wait, token);
                }
            }
            received.AddRange(result.Transactions.Where(x => x.Direction == TransferDirection.In && x.TransactedAtUtc >= from));
            if (!result.HasMore) return received;
        }
        throw new BankFeedException($"More than {PaymentFetcher.MaxPages} pages in {Window.TotalDays:0} days; the scan stopped.", false);
    }

    internal static string Title(string kind, long? amount)
    {
        var money = amount is { } value ? $" ({PaymentAlertBook.Money(value)})" : string.Empty;
        return kind switch
        {
            FindingKinds.MissedByPull => "Giao dịch bị bỏ lỡ, đối soát đã bắt lại" + money,
            FindingKinds.ChangedAfterStore => "SePay đổi dữ liệu giao dịch đã ghi" + money,
            FindingKinds.DoorbellWithoutTransfer => "Webhook có giao dịch nhưng API SePay không có" + money,
            FindingKinds.BookedWithoutReceipt => "Giao dịch đã ghi nhưng không có biên lai" + money,
            FindingKinds.ReceiptWithoutBookedTransfer => "Biên lai chuyển khoản không khớp giao dịch" + money,
            FindingKinds.ReceiptAmountMismatch => "Biên lai lệch số tiền với giao dịch" + money,
            FindingKinds.OverAllocated => "Biên lai bị trừ nợ quá số tiền" + money,
            FindingKinds.TotalsMismatch => "Tổng tiền vào và tổng biên lai không khớp" + money,
            _ => kind
        };
    }
}

/// <summary>
/// D10, checked every minute: failed transfers, SePay unreachable for an hour, a burst of bad signatures, transfers
/// stuck in Received, a unique conflict on receipts, changed or unknown provider data. Then the 07:00 digest.
/// </summary>
public sealed class PaymentMonitor(IPaymentOpsStore ops, PaymentAlertBook alerts, IClock clock)
{
    public static readonly TimeSpan ApiDownAfter = TimeSpan.FromHours(1);
    public static readonly TimeSpan StuckAfter = TimeSpan.FromMinutes(10);
    public const int SignatureBurst = 5;
    public static readonly TimeSpan SignatureWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan SignatureQuietAfter = TimeSpan.FromHours(24);
    public static readonly TimeOnly DigestAt = new(7, 0);

    /// <summary>Kinds the monitor decides alone: they close when their condition goes away.</summary>
    public static readonly string[] Resolvable =
        [AlertKinds.TransferFailed, AlertKinds.ApiDown, AlertKinds.BadSignatures, AlertKinds.StuckReceived, AlertKinds.ReceiptConflict];

    /// <param name="onlyCenterId">Limits alerts and the digest to one center (tests; a shared database has others).</param>
    public async Task<int> RunOnceAsync(CancellationToken token, Guid? onlyCenterId = null)
    {
        var now = clock.UtcNow;
        var snapshot = await ops.ReadMonitorSnapshotAsync(now, token);
        var raised = 0;
        foreach (var (centerId, drafts) in Decide(snapshot, now))
        {
            if (onlyCenterId is { } only && centerId != only) continue;
            raised += (await alerts.ApplyAsync(centerId, drafts, Resolvable, token)).Count;
        }

        var local = TimeZoneInfo.ConvertTime(now, FinanceTime.Zone);
        if (TimeOnly.FromDateTime(local.DateTime) >= DigestAt)
        {
            var today = DateOnly.FromDateTime(local.DateTime);
            foreach (var centerId in await ops.ListDigestDueAsync(today, token))
                if (onlyCenterId is null || centerId == onlyCenterId) await alerts.SendDigestAsync(centerId, today, token);
        }
        return raised;
    }

    /// <summary>Pure rules, one entry per center that has a connection or an open alert (so stale alerts close).</summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<AlertDraft>> Decide(MonitorSnapshot snapshot, DateTimeOffset now)
    {
        var result = new Dictionary<Guid, List<AlertDraft>>();
        List<AlertDraft> Of(Guid center) => result.TryGetValue(center, out var list) ? list : result[center] = [];
        foreach (var center in snapshot.CentersWithOpenAlerts) Of(center);

        foreach (var config in snapshot.Configs)
        {
            var drafts = Of(config.CenterId);
            if (config.Mode == PaymentProviderMode.Off) continue;
            var since = config.LastPulledAtUtc ?? config.CreatedAtUtc;
            if (config.LastPullError is { } error && since <= now - ApiDownAfter)
                drafts.Add(new AlertDraft($"api:{config.ConfigId}", AlertKinds.ApiDown, "Không lấy được giao dịch từ SePay quá 1 giờ",
                    $"Lần lấy thành công gần nhất: {(config.LastPulledAtUtc is { } at ? PaymentAlertBook.Local(at) : "chưa có")}. Lỗi: {error}. " +
                    "Tiền vẫn ở tài khoản và sẽ được ghi khi SePay hoạt động lại; nếu lỗi là token bị từ chối, hãy nhập lại API token."));

            var baseline = config.SignatureBaseline ?? 0;
            var burst = config.InvalidSignatureCount - baseline >= SignatureBurst &&
                        config.LastInvalidSignatureAtUtc is { } bad && bad > now - SignatureWindow;
            var quiet = config.LastInvalidSignatureAtUtc is not { } last || last <= now - SignatureQuietAfter;
            // Raised by a burst since the last alert; stays open until a day passes without a bad signature.
            if (burst || config.SignatureAlertOpen && !quiet)
                drafts.Add(new AlertDraft($"signatures:{config.ConfigId}", AlertKinds.BadSignatures,
                    "Nhiều webhook sai chữ ký", $"{config.InvalidSignatureCount} webhook bị từ chối vì sai chữ ký, gần nhất " +
                    $"{(config.LastInvalidSignatureAtUtc is { } seen ? PaymentAlertBook.Local(seen) : "—")}. Nếu bạn vừa đổi Secret Key, " +
                    "hãy cập nhật cả hai phía; nếu không, có thể ai đó đang thử gửi webhook giả (không gây mất tiền).",
                    burst ? config.InvalidSignatureCount : null));
        }

        foreach (var row in snapshot.Failed)
            Of(row.CenterId).Add(new AlertDraft($"failed:{row.Id}", AlertKinds.TransferFailed,
                $"Giao dịch {PaymentAlertBook.Money(row.Amount)} lỗi xử lý 5 lần",
                $"Nội dung “{row.Content}”, lúc {PaymentAlertBook.Local(row.TransactedAtUtc)}. Lỗi: {row.LastError}. " +
                "Sửa nguyên nhân rồi bấm Thử lại ở Học phí → Chuyển khoản."));
        foreach (var row in snapshot.Conflicts)
            Of(row.CenterId).Add(new AlertDraft($"conflict:{row.Id}", AlertKinds.ReceiptConflict,
                "Chặn được một lần ghi biên lai trùng",
                $"Giao dịch {PaymentAlertBook.Money(row.Amount)} “{row.Content}” suýt được ghi biên lai lần hai; khoá trong cơ sở dữ liệu đã chặn. " +
                "Lẽ ra không bao giờ xảy ra: hãy báo kỹ thuật."));
        foreach (var row in snapshot.Stuck)
            Of(row.CenterId).Add(new AlertDraft($"stuck:{row.ConfigId}", AlertKinds.StuckReceived,
                "Tiền vào nhưng chưa được ghi biên lai",
                $"{row.Count} giao dịch ({PaymentAlertBook.Money(row.Amount)}) chờ quá 10 phút, cũ nhất từ " +
                $"{PaymentAlertBook.Local(row.OldestSeenAtUtc)}. Worker thanh toán có thể đang dừng."));
        foreach (var item in snapshot.ProviderEvents)
            Of(item.CenterId).Add(item.Action == ProviderEventActions.Changed
                ? new AlertDraft($"{FindingKinds.ChangedAfterStore}:{item.TargetId}", AlertKinds.ProviderData,
                    "SePay đổi dữ liệu giao dịch đã ghi", item.Detail)
                : new AlertDraft($"field:{item.Detail}", AlertKinds.ProviderData, "SePay gửi trường dữ liệu mới",
                    $"Trường “{item.Detail}” chưa có trong danh sách đã kiểm tra. Hệ thống vẫn chạy; báo kỹ thuật xem có cần xử lý."));

        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<AlertDraft>)x.Value);
    }
}

/// <summary>Audit actions the inbox writes for the monitor (insert once per transfer or field).</summary>
public static class ProviderEventActions
{
    public const string Changed = "transaction_changed";
    public const string UnknownField = "provider_unknown_field";
}
