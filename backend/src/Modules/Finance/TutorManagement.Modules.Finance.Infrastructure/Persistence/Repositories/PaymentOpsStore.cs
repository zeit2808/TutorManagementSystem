using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
/// Đợt 5 reads and writes. Schedule columns and alerts are written with plain SQL: the lease guards the schedule, and
/// insert-or-reopen on the (center, fingerprint) key makes an alert open — and email — exactly once.
/// </summary>
public sealed class PaymentOpsStore(FinanceDbContext db, PaymentPullStore pulls) : IPaymentOpsStore, IPaymentOpsReader
{
    public async Task<PaymentMetricsDto> MetricsAsync(Guid centerId, DateTimeOffset now, int days, CancellationToken token)
    {
        var to = now.ToUniversalTime();
        var from = to.AddDays(-days);
        NpgsqlParameter[] Args() =>
        [
            P("center", NpgsqlDbType.Uuid, centerId), P("from", NpgsqlDbType.TimestampTz, from),
            P("to", NpgsqlDbType.TimestampTz, to), P("day", NpgsqlDbType.TimestampTz, to.AddHours(-24))
        ];
        var doorbells = await db.Database.SqlQueryRaw<PairRow>("""
            SELECT COUNT(*)::bigint AS "Left", COUNT(*) FILTER (WHERE d.delivery_count > 1)::bigint AS "Right"
            FROM finance.payment_webhook_deliveries d JOIN finance.payment_provider_configs c ON c.id = d.config_id
            WHERE c.center_id = @center AND d.first_seen_at_utc >= @from AND d.first_seen_at_utc <= @to
            """, Args()).SingleAsync(token);
        var hours = await db.Database.SqlQueryRaw<KeyCount>("""
            SELECT FLOOR(EXTRACT(EPOCH FROM (d.first_seen_at_utc - @day)) / 3600)::int::text AS "Key", COUNT(*)::int AS "Count"
            FROM finance.payment_webhook_deliveries d JOIN finance.payment_provider_configs c ON c.id = d.config_id
            WHERE c.center_id = @center AND d.first_seen_at_utc >= @day AND d.first_seen_at_utc <= @to
            GROUP BY 1
            """, Args()).ToListAsync(token);
        var byHour = new int[24];
        foreach (var hour in hours)
            if (int.TryParse(hour.Key, System.Globalization.CultureInfo.InvariantCulture, out var index))
                byHour[Math.Clamp(index, 0, 23)] += hour.Count;
        var statuses = await db.Database.SqlQueryRaw<KeyCount>("""
            SELECT status AS "Key", COUNT(*)::int AS "Count" FROM finance.bank_transactions
            WHERE center_id = @center AND first_seen_at_utc >= @from AND first_seen_at_utc <= @to GROUP BY status
            """, Args()).ToListAsync(token);
        var fetchedBy = await db.Database.SqlQueryRaw<KeyCount>("""
            SELECT fetched_by AS "Key", COUNT(*)::int AS "Count" FROM finance.bank_transactions
            WHERE center_id = @center AND first_seen_at_utc >= @from AND first_seen_at_utc <= @to GROUP BY fetched_by
            """, Args()).ToListAsync(token);
        var transfers = await db.Database.SqlQueryRaw<MetricsRow>("""
            SELECT COALESCE(SUM(amount), 0)::bigint AS "Amount",
                   COUNT(*) FILTER (WHERE seen_count > 1)::int AS "SeenAgain",
                   COUNT(*) FILTER (WHERE last_error LIKE '%ux_receipts_bank_transaction%')::int AS "Conflicts",
                   (SELECT EXTRACT(EPOCH FROM (@to - MIN(first_seen_at_utc)))::float8 FROM finance.bank_transactions
                    WHERE center_id = @center AND status = 'Received') AS "OldestReceived"
            FROM finance.bank_transactions
            WHERE center_id = @center AND first_seen_at_utc >= @from AND first_seen_at_utc <= @to
            """, Args()).SingleAsync(token);
        var latency = await db.Database.SqlQueryRaw<LatencyRow>("""
            SELECT percentile_cont(0.5) WITHIN GROUP (ORDER BY s)::float8 AS "P50",
                   percentile_cont(0.95) WITHIN GROUP (ORDER BY s)::float8 AS "P95", MAX(s)::float8 AS "Max"
            FROM (SELECT GREATEST(EXTRACT(EPOCH FROM (r.created_at_utc - t.transacted_at_utc)), 0) AS s
                  FROM finance.receipts r JOIN finance.bank_transactions t ON t.id = r.bank_transaction_id
                  WHERE t.center_id = @center AND r.source = 'Auto'
                    AND t.transacted_at_utc >= @from AND t.transacted_at_utc <= @to) x
            """, Args()).SingleAsync(token);
        var manual = await db.Database.SqlQueryRaw<KeyCount>("""
            SELECT action AS "Key", COUNT(*)::int AS "Count" FROM finance.payment_audit_events
            WHERE center_id = @center AND at_utc >= @from AND at_utc <= @to
              AND action IN ('transfer_assigned', 'transfer_ignored', 'transfer_retried')
            GROUP BY action
            """, Args()).ToListAsync(token);
        var config = await db.PaymentProviderConfigs.AsNoTracking().Where(x => x.CenterId == centerId)
            .Select(x => new { x.InvalidSignatureCount, x.LastInvalidSignatureAtUtc }).FirstOrDefaultAsync(token);
        var last = await db.ReconciliationRuns.AsNoTracking().Where(x => x.CenterId == centerId)
            .OrderByDescending(x => x.StartedAtUtc).FirstOrDefaultAsync(token);
        var openAlerts = await db.PaymentAlerts.CountAsync(x => x.CenterId == centerId && x.State == PaymentAlertState.Open, token);
        int Manual(string action) => manual.FirstOrDefault(x => x.Key == action)?.Count ?? 0;
        return new PaymentMetricsDto
        {
            Days = days, FromUtc = from, ToUtc = to,
            Doorbells = (int)doorbells.Left, DoorbellsRedelivered = (int)doorbells.Right, DoorbellsByHour = [.. byHour],
            InvalidSignatureTotal = config?.InvalidSignatureCount ?? 0, LastInvalidSignatureAtUtc = config?.LastInvalidSignatureAtUtc,
            Transfers = statuses.Sum(x => x.Count), TransferAmount = transfers.Amount,
            ByStatus = statuses.ToDictionary(x => x.Key, x => x.Count),
            ByFetchedBy = fetchedBy.ToDictionary(x => x.Key, x => x.Count),
            SeenAgain = transfers.SeenAgain, ReceiptConflicts = transfers.Conflicts,
            ManualAssigned = Manual("transfer_assigned"), ManualIgnored = Manual("transfer_ignored"),
            ManualRetried = Manual("transfer_retried"),
            LatencyP50Seconds = latency.P50, LatencyP95Seconds = latency.P95, LatencyMaxSeconds = latency.Max,
            OldestReceivedSeconds = transfers.OldestReceived, OpenAlerts = openAlerts,
            LastReconciledAtUtc = last?.FinishedAtUtc, LastReconciliationStatus = last?.Status.ToString(),
            LastReconciliationFindings = last?.FindingCount
        };
    }

    public sealed record KeyCount(string Key, int Count);

    public sealed record MetricsRow(long Amount, int SeenAgain, int Conflicts, double? OldestReceived);

    public sealed record LatencyRow(double? P50, double? P95, double? Max);

    public async Task<(IReadOnlyList<ReconciliationRun> Items, int Total)> ListRunsAsync(Guid centerId, int page, int pageSize,
        CancellationToken token)
    {
        var query = db.ReconciliationRuns.AsNoTracking().Where(x => x.CenterId == centerId);
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(x => x.StartedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(token);
        return (items, total);
    }

    public async Task<(ReconciliationRun Run, IReadOnlyList<ReconciliationFinding> Findings)?> FindRunAsync(Guid centerId,
        Guid runId, CancellationToken token)
    {
        var run = await db.ReconciliationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.CenterId == centerId, token);
        if (run is null) return null;
        var findings = await db.ReconciliationFindings.AsNoTracking().Where(x => x.RunId == runId)
            .OrderBy(x => x.Kind).ThenBy(x => x.Id).ToListAsync(token);
        return (run, findings);
    }

    public async Task<IReadOnlyList<PaymentAlert>> ListAlertsAsync(Guid centerId, PaymentAlertState? state, int max,
        CancellationToken token)
    {
        var query = db.PaymentAlerts.AsNoTracking().Where(x => x.CenterId == centerId);
        if (state is { } wanted) query = query.Where(x => x.State == wanted);
        return await query.OrderByDescending(x => x.LastSeenAtUtc).Take(max).ToListAsync(token);
    }

    public Task<PaymentAlert?> FindAlertAsync(Guid centerId, Guid alertId, CancellationToken token) =>
        db.PaymentAlerts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == alertId && x.CenterId == centerId, token);

    public async Task<bool> AcknowledgeAlertAsync(Guid centerId, Guid alertId, Guid actorId, DateTimeOffset now,
        CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var at = now.ToUniversalTime();
        var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_alerts SET state = 'Acknowledged', acknowledged_by_user_id = {actorId}, acknowledged_at_utc = {at}
            WHERE id = {alertId} AND center_id = {centerId} AND state = 'Open'
            """, token);
        if (updated == 0) return false;
        db.PaymentAuditEvents.Add(PaymentAuditEvent.Record(centerId, actorId, "alert_acknowledged", "payment_alert", alertId,
            null, null, now));
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<IReadOnlyList<ReconcileTarget>> ClaimDueReconciliationsAsync(Guid leaseId, DateTimeOffset now,
        DateTimeOffset firstNightly, TimeSpan lease, int max, Guid? onlyConfigId, CancellationToken token)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_provider_configs SET next_reconcile_at_utc = {firstNightly.ToUniversalTime()}
            WHERE mode <> 'Off' AND next_reconcile_at_utc IS NULL AND ({onlyConfigId}::uuid IS NULL OR id = {onlyConfigId})
            """, token);
        var rows = await db.Database.SqlQueryRaw<TargetRow>("""
            UPDATE finance.payment_provider_configs c
            SET reconcile_processing_id = @lease, reconcile_processing_until_utc = @until
            FROM (
                SELECT id FROM finance.payment_provider_configs
                WHERE mode <> 'Off' AND next_reconcile_at_utc <= @now
                  AND (reconcile_processing_until_utc IS NULL OR reconcile_processing_until_utc < @now)
                  AND (@only::uuid IS NULL OR id = @only)
                ORDER BY next_reconcile_at_utc
                LIMIT @max
                FOR UPDATE SKIP LOCKED) due
            WHERE c.id = due.id
            RETURNING c.id AS "Id", c.center_id AS "CenterId", c.provider AS "Provider", c.api_token AS "ApiToken",
                      c.key_id AS "KeyId", c.created_at_utc AS "CreatedAtUtc", c.reconcile_requested_at_utc AS "RequestedAtUtc"
            """,
            P("lease", NpgsqlDbType.Uuid, leaseId),
            P("until", NpgsqlDbType.TimestampTz, now.Add(lease).ToUniversalTime()),
            P("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime()),
            P("max", NpgsqlDbType.Integer, max),
            P("only", NpgsqlDbType.Uuid, onlyConfigId)).ToListAsync(token);
        return rows.Select(x => new ReconcileTarget(x.Id, x.CenterId, Enum.Parse<PaymentProvider>(x.Provider), x.ApiToken, x.KeyId,
            x.CreatedAtUtc, x.RequestedAtUtc)).ToArray();
    }

    public async Task<StoredScan> StoreScanAsync(Guid configId, IReadOnlyList<ReceivedBankTransaction> incoming,
        FetchedBy fetchedBy, DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var stored = await pulls.StoreAsync(configId, incoming, fetchedBy, now, token);
        await transaction.CommitAsync(token);
        return new StoredScan([.. stored.Inserted, .. stored.Held], stored.Changed);
    }

    public async Task<IReadOnlyList<FindingDraft>> CheckLedgerAsync(Guid centerId, DateTimeOffset now, CancellationToken token)
    {
        var center = P("center", NpgsqlDbType.Uuid, centerId);
        var findings = new List<FindingDraft>();

        foreach (var row in await Rows("""
            SELECT t.id AS "TransferId", NULL::uuid AS "ReceiptId", t.amount AS "Amount", t.content AS "Text"
            FROM finance.bank_transactions t
            WHERE t.center_id = @center AND t.status = 'Booked'
              AND NOT EXISTS (SELECT 1 FROM finance.receipts r WHERE r.bank_transaction_id = t.id AND r.voided_at_utc IS NULL)
            """, center))
            findings.Add(new FindingDraft(FindingKinds.BookedWithoutReceipt, row.TransferId, null, null, row.Amount,
                $"Giao dịch “{row.Text}” đã ghi nhận nhưng không có biên lai còn hiệu lực."));

        foreach (var row in await Rows("""
            SELECT t.id AS "TransferId", r.id AS "ReceiptId", r.amount AS "Amount",
                   'PT-' || lpad(r.number::text, 6, '0') || ' · ' || COALESCE(t.status, 'không có giao dịch') AS "Text"
            FROM finance.receipts r
            LEFT JOIN finance.bank_transactions t ON t.id = r.bank_transaction_id
            WHERE r.center_id = @center AND r.voided_at_utc IS NULL
              AND ((r.bank_transaction_id IS NOT NULL AND (t.id IS NULL OR t.status <> 'Booked'))
                   OR (r.source = 'Auto' AND r.bank_transaction_id IS NULL))
            """, center))
            findings.Add(new FindingDraft(FindingKinds.ReceiptWithoutBookedTransfer, row.TransferId, row.ReceiptId, null,
                row.Amount, $"Biên lai {row.Text}: biên lai chuyển khoản phải gắn với một giao dịch đã ghi."));

        foreach (var row in await Rows("""
            SELECT t.id AS "TransferId", r.id AS "ReceiptId", r.amount AS "Amount",
                   'PT-' || lpad(r.number::text, 6, '0') || ' ghi ' || r.amount::text || ', giao dịch ' || t.amount::text AS "Text"
            FROM finance.receipts r
            JOIN finance.bank_transactions t ON t.id = r.bank_transaction_id
            WHERE r.center_id = @center AND r.voided_at_utc IS NULL AND r.amount <> t.amount
            """, center))
            findings.Add(new FindingDraft(FindingKinds.ReceiptAmountMismatch, row.TransferId, row.ReceiptId, null, row.Amount,
                $"Biên lai {row.Text} (đồng)."));

        foreach (var row in await Rows("""
            SELECT NULL::uuid AS "TransferId", r.id AS "ReceiptId", r.amount AS "Amount",
                   'PT-' || lpad(r.number::text, 6, '0') || ' trừ nợ ' || SUM(a.amount)::text AS "Text"
            FROM finance.receipts r
            JOIN finance.receipt_allocations a ON a.receipt_id = r.id
            WHERE r.center_id = @center AND r.voided_at_utc IS NULL
            GROUP BY r.id, r.amount, r.number
            HAVING SUM(a.amount) > r.amount OR MIN(a.amount) < 0
            """, center))
            findings.Add(new FindingDraft(FindingKinds.OverAllocated, null, row.ReceiptId, null, row.Amount,
                $"Biên lai {row.Text} (đồng), nhiều hơn số tiền của biên lai."));

        // A signed webhook names a bank reference; the API must have that transfer within an hour. Outgoing money is
        // never stored, so only incoming webhooks are compared.
        foreach (var row in await Rows("""
            SELECT d.id AS "TransferId", NULL::uuid AS "ReceiptId", COALESCE(d.amount, 0) AS "Amount",
                   d.reference_code AS "Text"
            FROM finance.payment_webhook_deliveries d
            JOIN finance.payment_provider_configs c ON c.id = d.config_id
            WHERE c.center_id = @center AND d.reference_code IS NOT NULL AND d.first_seen_at_utc < @hour
              AND COALESCE(d.raw_body::jsonb ->> 'transferType', 'in') = 'in'
              AND NOT EXISTS (SELECT 1 FROM finance.bank_transactions t
                              WHERE t.config_id = d.config_id AND t.reference_code = d.reference_code
                                AND (d.amount IS NULL OR t.amount = d.amount))
            """, center, P("hour", NpgsqlDbType.TimestampTz, now.AddHours(-1).ToUniversalTime())))
            findings.Add(new FindingDraft(FindingKinds.DoorbellWithoutTransfer, null, null, row.TransferId, row.Amount,
                $"SePay gửi webhook ký đúng cho mã ngân hàng {row.Text} nhưng API không có giao dịch tương ứng. " +
                "Đối chiếu sao kê ngân hàng; nếu tiền đã vào, ghi tay biên lai."));

        // K17 over the whole history: Σ Booked transfers = Σ their live receipts.
        var totals = await db.Database.SqlQueryRaw<PairRow>("""
            SELECT COALESCE((SELECT SUM(amount) FROM finance.bank_transactions WHERE center_id = @center AND status = 'Booked'), 0)::bigint AS "Left",
                   COALESCE((SELECT SUM(r.amount) FROM finance.receipts r JOIN finance.bank_transactions t ON t.id = r.bank_transaction_id
                             WHERE t.center_id = @center AND t.status = 'Booked' AND r.voided_at_utc IS NULL), 0)::bigint AS "Right"
            """, P("center", NpgsqlDbType.Uuid, centerId)).SingleAsync(token);
        if (totals.Left != totals.Right)
            findings.Add(new FindingDraft(FindingKinds.TotalsMismatch, null, null, null, totals.Left - totals.Right,
                $"Tổng giao dịch đã ghi {totals.Left} đồng, tổng biên lai của chúng {totals.Right} đồng."));
        return findings;

        async Task<List<CheckRow>> Rows(string sql, params NpgsqlParameter[] parameters) =>
            await db.Database.SqlQueryRaw<CheckRow>(sql, parameters.Select(Clone).ToArray()).ToListAsync(token);
    }

    public async Task<LedgerTotals> TotalsAsync(Guid configId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token)
    {
        var row = await db.Database.SqlQueryRaw<TotalsRow>("""
            SELECT COALESCE(SUM(amount), 0)::bigint AS "Incoming",
                   COALESCE(SUM(amount) FILTER (WHERE status = 'Booked'), 0)::bigint AS "Booked",
                   COALESCE(SUM(amount) FILTER (WHERE status IN ('Received', 'NeedsReview', 'Held')), 0)::bigint AS "Pending",
                   COALESCE(SUM(amount) FILTER (WHERE status = 'Failed'), 0)::bigint AS "Failed",
                   COALESCE(SUM(amount) FILTER (WHERE status = 'Ignored'), 0)::bigint AS "Ignored",
                   COALESCE((SELECT SUM(r.amount) FROM finance.receipts r
                             JOIN finance.bank_transactions b ON b.id = r.bank_transaction_id
                             WHERE b.config_id = @config AND b.status = 'Booked' AND r.voided_at_utc IS NULL
                               AND b.transacted_at_utc >= @from AND b.transacted_at_utc <= @to), 0)::bigint AS "BookedReceipts"
            FROM finance.bank_transactions
            WHERE config_id = @config AND transacted_at_utc >= @from AND transacted_at_utc <= @to
            """,
            P("config", NpgsqlDbType.Uuid, configId),
            P("from", NpgsqlDbType.TimestampTz, fromUtc.ToUniversalTime()),
            P("to", NpgsqlDbType.TimestampTz, toUtc.ToUniversalTime())).SingleAsync(token);
        return new LedgerTotals(row.Incoming, row.Booked, row.BookedReceipts, row.Pending, row.Failed, row.Ignored);
    }

    public async Task<bool> FinishReconciliationAsync(ReconcileTarget target, Guid leaseId, ReconciliationRun run,
        IReadOnlyList<ReconciliationFinding> findings, DateTimeOffset nextNightly, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        // A request that came in while this run was going asks for another run at once.
        var updated = await db.Database.ExecuteSqlRawAsync("""
            UPDATE finance.payment_provider_configs
            SET next_reconcile_at_utc = CASE WHEN reconcile_requested_at_utc > COALESCE(@claimed, '-infinity'::timestamptz) THEN @now ELSE @next END,
                reconcile_requested_at_utc = CASE WHEN reconcile_requested_at_utc > COALESCE(@claimed, '-infinity'::timestamptz)
                                                  THEN reconcile_requested_at_utc ELSE NULL END,
                reconcile_processing_id = NULL, reconcile_processing_until_utc = NULL
            WHERE id = @id AND reconcile_processing_id = @lease
            """, [
            P("claimed", NpgsqlDbType.TimestampTz, target.RequestedAtUtc?.ToUniversalTime()),
            P("now", NpgsqlDbType.TimestampTz, run.FinishedAtUtc.ToUniversalTime()),
            P("next", NpgsqlDbType.TimestampTz, nextNightly.ToUniversalTime()),
            P("id", NpgsqlDbType.Uuid, target.ConfigId),
            P("lease", NpgsqlDbType.Uuid, leaseId)
        ], token);
        if (updated == 0)
        {
            await transaction.RollbackAsync(token);
            return false;
        }
        db.ReconciliationRuns.Add(run);
        db.ReconciliationFindings.AddRange(findings);
        await db.SaveChangesAsync(token);
        db.ChangeTracker.Clear();
        await transaction.CommitAsync(token);
        return true;
    }

    public async Task<bool> RequestReconciliationAsync(Guid centerId, DateTimeOffset now, CancellationToken token) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_provider_configs
            SET reconcile_requested_at_utc = {now.ToUniversalTime()}, next_reconcile_at_utc = {now.ToUniversalTime()}
            WHERE center_id = {centerId} AND mode <> 'Off'
            """, token) > 0;

    public async Task<ITuitionGuard> BeginAlertScopeAsync(Guid centerId, CancellationToken token)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var key = $"finance-alerts:{centerId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new Guard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<RaisedAlert>> ApplyAlertsAsync(Guid centerId, IReadOnlyList<AlertDraft> drafts,
        IReadOnlyCollection<string> resolvableKinds, DateTimeOffset now, CancellationToken token)
    {
        var raised = new List<RaisedAlert>();
        var at = now.ToUniversalTime();
        foreach (var draft in drafts.DistinctBy(x => x.Fingerprint))
        {
            var fingerprint = Cut(draft.Fingerprint, PaymentAlert.MaxFingerprintLength);
            var title = Cut(draft.Title, PaymentAlert.MaxTitleLength);
            var detail = Cut(draft.Detail, PaymentAlert.MaxDetailLength);
            var opened = await db.Database.SqlQueryRaw<Guid>("""
                INSERT INTO finance.payment_alerts (id, center_id, fingerprint, kind, title, detail, state, metric,
                    first_seen_at_utc, last_seen_at_utc, last_emailed_at_utc)
                VALUES (@id, @center, @fingerprint, @kind, @title, @detail, 'Open', @metric, @now, @now, @now)
                ON CONFLICT (center_id, fingerprint) DO NOTHING
                RETURNING id AS "Value"
                """,
                P("id", NpgsqlDbType.Uuid, Guid.NewGuid()), P("center", NpgsqlDbType.Uuid, centerId),
                P("fingerprint", NpgsqlDbType.Varchar, fingerprint), P("kind", NpgsqlDbType.Varchar, draft.Kind),
                P("title", NpgsqlDbType.Varchar, title), P("detail", NpgsqlDbType.Varchar, detail),
                P("metric", NpgsqlDbType.Bigint, draft.Metric), P("now", NpgsqlDbType.TimestampTz, at)).ToListAsync(token);
            if (opened.Count == 0)
                opened = await db.Database.SqlQueryRaw<Guid>("""
                    UPDATE finance.payment_alerts
                    SET state = 'Open', title = @title, detail = @detail, metric = COALESCE(@metric, metric),
                        first_seen_at_utc = @now, last_seen_at_utc = @now, last_emailed_at_utc = @now,
                        resolved_at_utc = NULL, acknowledged_by_user_id = NULL, acknowledged_at_utc = NULL
                    WHERE center_id = @center AND fingerprint = @fingerprint AND state = 'Resolved'
                    RETURNING id AS "Value"
                    """,
                    P("center", NpgsqlDbType.Uuid, centerId), P("fingerprint", NpgsqlDbType.Varchar, fingerprint),
                    P("title", NpgsqlDbType.Varchar, title), P("detail", NpgsqlDbType.Varchar, detail),
                    P("metric", NpgsqlDbType.Bigint, draft.Metric), P("now", NpgsqlDbType.TimestampTz, at)).ToListAsync(token);
            if (opened.Count > 0)
            {
                raised.Add(new RaisedAlert(opened[0], draft.Kind, title, detail));
                continue;
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE finance.payment_alerts SET last_seen_at_utc = {at}, detail = {detail}
                WHERE center_id = {centerId} AND fingerprint = {fingerprint}
                """, token);
        }

        if (resolvableKinds.Count > 0)
        {
            var kinds = resolvableKinds.ToArray();
            var keep = drafts.Select(x => Cut(x.Fingerprint, PaymentAlert.MaxFingerprintLength)).ToArray();
            await db.Database.ExecuteSqlRawAsync("""
                UPDATE finance.payment_alerts SET state = 'Resolved', resolved_at_utc = @now
                WHERE center_id = @center AND kind = ANY(@kinds) AND state IN ('Open', 'Acknowledged')
                  AND NOT (fingerprint = ANY(@keep))
                """, [
                P("now", NpgsqlDbType.TimestampTz, at), P("center", NpgsqlDbType.Uuid, centerId),
                new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = kinds },
                new NpgsqlParameter("keep", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = keep }
            ], token);
        }
        return raised;
    }

    public async Task<MonitorSnapshot> ReadMonitorSnapshotAsync(DateTimeOffset now, CancellationToken token)
    {
        var configs = await db.Database.SqlQueryRaw<MonitorConfigRow>("""
            SELECT c.id AS "ConfigId", c.center_id AS "CenterId", c.mode AS "Mode", c.last_pull_error AS "LastPullError",
                   c.last_pulled_at_utc AS "LastPulledAtUtc", c.created_at_utc AS "CreatedAtUtc",
                   c.invalid_signature_count AS "InvalidSignatureCount", c.last_invalid_signature_at_utc AS "LastInvalidSignatureAtUtc",
                   a.metric AS "SignatureBaseline", COALESCE(a.state IN ('Open', 'Acknowledged'), false) AS "SignatureAlertOpen"
            FROM finance.payment_provider_configs c
            LEFT JOIN finance.payment_alerts a ON a.center_id = c.center_id AND a.fingerprint = 'signatures:' || c.id::text
            """).ToListAsync(token);
        var failed = await Transfers("""
            SELECT id AS "Id", center_id AS "CenterId", amount AS "Amount", content AS "Content",
                   transacted_at_utc AS "TransactedAtUtc", last_error AS "LastError"
            FROM finance.bank_transactions WHERE status = 'Failed'
            """);
        var conflicts = await Transfers("""
            SELECT id AS "Id", center_id AS "CenterId", amount AS "Amount", content AS "Content",
                   transacted_at_utc AS "TransactedAtUtc", last_error AS "LastError"
            FROM finance.bank_transactions
            WHERE status IN ('Received', 'Failed') AND last_error LIKE '%ux_receipts_bank_transaction%'
            """);
        var stuck = await db.Database.SqlQueryRaw<MonitorStuck>("""
            SELECT b.config_id AS "ConfigId", b.center_id AS "CenterId", COUNT(*)::int AS "Count", SUM(b.amount)::bigint AS "Amount",
                   MIN(b.first_seen_at_utc) AS "OldestSeenAtUtc"
            FROM finance.bank_transactions b
            JOIN finance.payment_provider_configs c ON c.id = b.config_id
            WHERE c.mode = 'Auto' AND b.status = 'Received' AND b.first_seen_at_utc < @stuck
            GROUP BY b.config_id, b.center_id
            """, P("stuck", NpgsqlDbType.TimestampTz, now.Subtract(PaymentMonitor.StuckAfter).ToUniversalTime())).ToListAsync(token);
        var events = await db.Database.SqlQueryRaw<EventRow>("""
            SELECT center_id AS "CenterId", action AS "Action", target_id AS "TargetId", after_json::text AS "Json"
            FROM finance.payment_audit_events
            WHERE action IN (@changed, @field) AND at_utc > @since
            """,
            P("changed", NpgsqlDbType.Varchar, ProviderEventActions.Changed),
            P("field", NpgsqlDbType.Varchar, ProviderEventActions.UnknownField),
            P("since", NpgsqlDbType.TimestampTz, now.AddDays(-7).ToUniversalTime())).ToListAsync(token);
        var open = await db.Database.SqlQueryRaw<Guid>("""
            SELECT DISTINCT center_id AS "Value" FROM finance.payment_alerts
            WHERE state IN ('Open', 'Acknowledged') AND kind = ANY(@kinds)
            """, new NpgsqlParameter("kinds", NpgsqlDbType.Array | NpgsqlDbType.Varchar) { Value = PaymentMonitor.Resolvable })
            .ToListAsync(token);

        return new MonitorSnapshot(
            configs.Select(x => new MonitorConfig(x.ConfigId, x.CenterId, Enum.Parse<PaymentProviderMode>(x.Mode), x.LastPullError,
                x.LastPulledAtUtc, x.CreatedAtUtc, x.InvalidSignatureCount, x.LastInvalidSignatureAtUtc, x.SignatureBaseline,
                x.SignatureAlertOpen)).ToArray(),
            failed, conflicts, stuck, events.Select(Event).ToArray(), open);

        async Task<List<MonitorTransfer>> Transfers(string sql) => await db.Database.SqlQueryRaw<MonitorTransfer>(sql).ToListAsync(token);
    }

    public async Task<IReadOnlyList<Guid>> ListDigestDueAsync(DateOnly today, CancellationToken token) =>
        await db.Database.SqlQueryRaw<Guid>("""
            SELECT DISTINCT center_id AS "Value" FROM finance.payment_provider_configs
            WHERE mode <> 'Off' AND (digest_sent_on IS NULL OR digest_sent_on < @today)
            """, new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today }).ToListAsync(token);

    public async Task<(bool Claimed, IReadOnlyList<OpenAlert> Alerts, DigestBacklog Queue)> ClaimDigestAsync(Guid centerId,
        DateOnly today, DateTimeOffset now, CancellationToken token)
    {
        var claimed = await db.Database.ExecuteSqlRawAsync("""
            UPDATE finance.payment_provider_configs SET digest_sent_on = @today
            WHERE center_id = @center AND mode <> 'Off' AND (digest_sent_on IS NULL OR digest_sent_on < @today)
            """, [new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today }, P("center", NpgsqlDbType.Uuid, centerId)], token);
        if (claimed == 0) return (false, [], new DigestBacklog(0, 0, 0, 0));

        var alerts = await db.PaymentAlerts.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.State == PaymentAlertState.Open)
            .OrderBy(x => x.FirstSeenAtUtc).Take(50)
            .Select(x => new OpenAlert(x.Id, x.Kind, x.Title, x.Detail, x.FirstSeenAtUtc)).ToListAsync(token);
        if (alerts.Count > 0)
        {
            var ids = alerts.Select(x => x.Id).ToArray();
            await db.Database.ExecuteSqlRawAsync("UPDATE finance.payment_alerts SET last_emailed_at_utc = @now WHERE id = ANY(@ids)",
                [P("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime()), new NpgsqlParameter("ids", ids)], token);
        }
        var queue = await db.Database.SqlQueryRaw<DigestBacklog>("""
            SELECT COUNT(*) FILTER (WHERE status = 'NeedsReview')::int AS "NeedsReview",
                   COUNT(*) FILTER (WHERE status = 'Held')::int AS "Held",
                   COUNT(*) FILTER (WHERE status = 'Failed')::int AS "Failed",
                   COALESCE(SUM(amount), 0)::bigint AS "Amount"
            FROM finance.bank_transactions
            WHERE center_id = @center AND status IN ('NeedsReview', 'Held', 'Failed') AND first_seen_at_utc < @day
            """, P("center", NpgsqlDbType.Uuid, centerId), P("day", NpgsqlDbType.TimestampTz, now.AddDays(-1).ToUniversalTime()))
            .SingleAsync(token);
        return (true, alerts, queue);
    }

    private static MonitorProviderEvent Event(EventRow row)
    {
        using var json = JsonDocument.Parse(row.Json ?? "{}");
        var root = json.RootElement;
        string Text(string name) => root.TryGetProperty(name, out var value) ? value.ToString() : string.Empty;
        return row.Action == ProviderEventActions.Changed
            ? new MonitorProviderEvent(row.CenterId, row.Action, row.TargetId,
                $"Giao dịch SePay {Text("providerTxnId")} nay được báo là {Text("amount")} đồng, nội dung “{Text("content")}”, " +
                $"mã ngân hàng {Text("referenceCode")}. Hệ thống giữ bản đã ghi; hãy đối chiếu sao kê ngân hàng.")
            : new MonitorProviderEvent(row.CenterId, row.Action, null, Text("field"));
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    private static NpgsqlParameter P(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    private static NpgsqlParameter Clone(NpgsqlParameter p) => new(p.ParameterName, p.NpgsqlDbType) { Value = p.Value };

    private sealed class Guard(IDbContextTransaction transaction) : ITuitionGuard
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }

    public sealed record TargetRow(Guid Id, Guid CenterId, string Provider, byte[] ApiToken, string KeyId,
        DateTimeOffset CreatedAtUtc, DateTimeOffset? RequestedAtUtc);

    public sealed record CheckRow(Guid? TransferId, Guid? ReceiptId, long Amount, string? Text);

    public sealed record PairRow(long Left, long Right);

    public sealed record TotalsRow(long Incoming, long Booked, long Pending, long Failed, long Ignored, long BookedReceipts);

    public sealed record MonitorConfigRow(Guid ConfigId, Guid CenterId, string Mode, string? LastPullError,
        DateTimeOffset? LastPulledAtUtc, DateTimeOffset CreatedAtUtc, long InvalidSignatureCount,
        DateTimeOffset? LastInvalidSignatureAtUtc, long? SignatureBaseline, bool SignatureAlertOpen);

    public sealed record EventRow(Guid CenterId, string Action, Guid? TargetId, string? Json);
}
