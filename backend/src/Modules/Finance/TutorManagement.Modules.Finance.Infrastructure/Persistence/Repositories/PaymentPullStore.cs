using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
/// Inbox writes for the fetcher. Schedule columns are written with plain SQL guarded by the lease, so an owner editing
/// the config at the same time (EF, version checked) and the Worker never overwrite each other's columns.
/// </summary>
public sealed class PaymentPullStore(FinanceDbContext db) : IPaymentPullStore
{
    public async Task<IReadOnlyList<PullTarget>> ClaimDueAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max,
        CancellationToken token)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(token);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE finance.payment_provider_configs c
                SET pull_processing_id = @lease, pull_processing_until_utc = @until
                FROM (
                    SELECT id FROM finance.payment_provider_configs
                    WHERE mode <> 'Off'
                      AND COALESCE(next_pull_at_utc, '-infinity'::timestamptz) <= @now
                      AND (last_pulled_at_utc IS NULL OR last_pulled_at_utc <= @spaced)
                      AND (pull_processing_until_utc IS NULL OR pull_processing_until_utc < @now)
                    ORDER BY next_pull_at_utc NULLS FIRST
                    LIMIT @max
                    FOR UPDATE SKIP LOCKED) due
                WHERE c.id = due.id
                RETURNING c.id, c.center_id, c.provider, c.api_token, c.key_id, c.pull_cursor, c.created_at_utc,
                          c.pull_requested_at_utc, c.last_pulled_at_utc, c.pull_retry_count
                """;
            Add(command, "lease", NpgsqlDbType.Uuid, leaseId);
            Add(command, "until", NpgsqlDbType.TimestampTz, now.Add(lease).ToUniversalTime());
            Add(command, "now", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            Add(command, "spaced", NpgsqlDbType.TimestampTz, now.AddSeconds(-1).ToUniversalTime());
            Add(command, "max", NpgsqlDbType.Integer, max);
            var targets = new List<PullTarget>();
            await using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
                targets.Add(new PullTarget(
                    reader.GetGuid(0), reader.GetGuid(1), Enum.Parse<PaymentProvider>(reader.GetString(2)),
                    (byte[])reader[3], reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                    reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                    reader.GetInt32(9)));
            return targets;
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    public async Task<PullResult?> SaveAsync(PullTarget target, Guid leaseId, IReadOnlyList<ReceivedBankTransaction> incoming,
        FetchedBy fetchedBy, string? newCursor, PullSchedule schedule, DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var config = await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == target.ConfigId, token);
        int inserted = 0, seen = 0, held = 0, changed = 0;

        // Oldest first, so the first copy of a bank reference keeps the key and a later provider id is the duplicate.
        foreach (var item in incoming.OrderBy(x => x.TransactedAtUtc))
        {
            var existing = await db.BankTransactions.AsNoTracking()
                .Where(x => x.Provider == target.Provider && x.ProviderTxnId == item.ProviderTxnId)
                .Select(x => new { x.Id, x.PayloadChecksum }).SingleOrDefaultAsync(token);
            if (existing is not null)
            {
                seen++;
                if (existing.PayloadChecksum != BankTransactionKeys.Checksum(item)) changed++;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE finance.bank_transactions SET seen_count = seen_count + 1, last_seen_at_utc = {now}
                    WHERE id = {existing.Id}
                    """, token);
                continue;
            }

            var outcome = await InsertAsync(config, item, fetchedBy, now, token);
            if (outcome == Insert.Seen) seen++;
            else if (outcome == Insert.Held) held++;
            else inserted++;
        }

        // A doorbell that rang while this pull ran asks for another pull at once.
        var updated = await db.Database.ExecuteSqlRawAsync("""
            UPDATE finance.payment_provider_configs
            SET pull_cursor = @cursor,
                last_pulled_at_utc = @now,
                pull_retry_count = CASE WHEN pull_requested_at_utc > COALESCE(@rang, '-infinity'::timestamptz) THEN 0 ELSE @retry END,
                next_pull_at_utc = CASE WHEN pull_requested_at_utc > COALESCE(@rang, '-infinity'::timestamptz) THEN @now ELSE @next END,
                last_pull_error = NULL,
                pull_processing_id = NULL,
                pull_processing_until_utc = NULL
            WHERE id = @id AND pull_processing_id = @lease
            """, [
            Parameter("cursor", NpgsqlDbType.Varchar, newCursor),
            Parameter("now", NpgsqlDbType.TimestampTz, now.ToUniversalTime()),
            Parameter("rang", NpgsqlDbType.TimestampTz, target.PullRequestedAtUtc?.ToUniversalTime()),
            Parameter("retry", NpgsqlDbType.Integer, schedule.RetryCount),
            Parameter("next", NpgsqlDbType.TimestampTz, schedule.NextPullAtUtc.ToUniversalTime()),
            Parameter("id", NpgsqlDbType.Uuid, target.ConfigId),
            Parameter("lease", NpgsqlDbType.Uuid, leaseId)
        ], token);
        if (updated == 0)
        {
            await transaction.RollbackAsync(token);
            db.ChangeTracker.Clear();
            return null;
        }
        await transaction.CommitAsync(token);
        return new PullResult(inserted, seen, held, changed);
    }

    public Task FailAsync(PullTarget target, Guid leaseId, string error, DateTimeOffset nextPullAtUtc, DateTimeOffset now,
        CancellationToken token)
    {
        var message = error.Length <= 500 ? error : error[..500];
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_provider_configs
            SET last_pull_error = {message}, next_pull_at_utc = {nextPullAtUtc.ToUniversalTime()},
                pull_processing_id = NULL, pull_processing_until_utc = NULL
            WHERE id = {target.ConfigId} AND pull_processing_id = {leaseId}
            """, token);
    }

    private enum Insert { New, Held, Seen }

    /// <summary>
    /// Inserts under the two keys. EF saves inside a savepoint, so a unique violation only undoes this row:
    /// a bank reference already taken makes the row Held, a provider id already taken means another pull got it first.
    /// </summary>
    private async Task<Insert> InsertAsync(PaymentProviderConfig config, ReceivedBankTransaction item, FetchedBy fetchedBy,
        DateTimeOffset now, CancellationToken token)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var key = BankTransactionKeys.SemanticKey(item.BankCode, item.AccountNumber, item.ReferenceCode);
            Guid? original = key is null
                ? null
                : await db.BankTransactions.AsNoTracking().Where(x => x.SemanticKey == key).Select(x => (Guid?)x.Id)
                    .SingleOrDefaultAsync(token);
            var row = original is { } id
                ? BankTransaction.HoldAsDuplicate(config, item, id, fetchedBy, now)
                : BankTransaction.Receive(config, item, fetchedBy, now);
            db.BankTransactions.Add(row);
            if (original is { } heldOf)
                db.PaymentAuditEvents.Add(PaymentAuditEvent.Record(config.CenterId, null, "transaction_held", "bank_transaction",
                    row.Id, null, JsonSerializer.Serialize(new { duplicateOf = heldOf, providerTxnId = item.ProviderTxnId }), now));
            try
            {
                await db.SaveChangesAsync(token);
                db.ChangeTracker.Clear();
                return original is null ? Insert.New : Insert.Held;
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
            {
                db.ChangeTracker.Clear();
                if (pg.ConstraintName == "ux_bank_transactions_provider_txn") return Insert.Seen;
                if (pg.ConstraintName != "ux_bank_transactions_semantic_key") throw;
                // Another config stored the same bank reference a moment ago: look again and hold this copy.
            }
        }
        throw new InvalidOperationException($"Could not store transaction {item.ProviderTxnId}.");
    }

    private static void Add(DbCommand command, string name, NpgsqlDbType type, object value) =>
        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value });

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };
}
