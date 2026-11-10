using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>Inbox reads and lease-guarded writes for the booker; plain SQL so staff edits and the Worker never collide.</summary>
public sealed class PaymentBookingStore(FinanceDbContext db) : IPaymentBookingStore
{
    /// <summary>After this many failures a transfer is Failed: the owner is told and retries it by hand.</summary>
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60)];

    public async Task<IReadOnlyList<BankTransaction>> ClaimShadowAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max,
        CancellationToken token)
    {
        var ids = await db.Database.SqlQueryRaw<Guid>("""
            UPDATE finance.bank_transactions t
            SET processing_id = @lease, processing_until_utc = @until
            FROM (
                SELECT b.id FROM finance.bank_transactions b
                JOIN finance.payment_provider_configs c ON c.id = b.config_id
                WHERE b.status = 'Received' AND b.shadow_evaluated_at_utc IS NULL AND c.mode = 'Shadow'
                  AND (b.next_attempt_at_utc IS NULL OR b.next_attempt_at_utc <= @now)
                  AND (b.processing_until_utc IS NULL OR b.processing_until_utc < @now)
                ORDER BY b.transacted_at_utc
                LIMIT @max
                FOR UPDATE OF b SKIP LOCKED) due
            WHERE t.id = due.id
            RETURNING t.id AS "Value"
            """,
            new NpgsqlParameter("lease", NpgsqlDbType.Uuid) { Value = leaseId },
            new NpgsqlParameter("until", NpgsqlDbType.TimestampTz) { Value = now.Add(lease).ToUniversalTime() },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() },
            new NpgsqlParameter("max", NpgsqlDbType.Integer) { Value = max }).ToListAsync(token);
        if (ids.Count == 0) return [];
        return await db.BankTransactions.AsNoTracking().Where(x => ids.Contains(x.Id)).OrderBy(x => x.TransactedAtUtc).ToListAsync(token);
    }

    public async Task<(string? ConfigAccount, IReadOnlyCollection<string> IntentAccounts, IReadOnlyList<IntentRef> Intents)> LoadIntentsAsync(
        Guid centerId, Guid configId, IReadOnlyCollection<string> codes, CancellationToken token)
    {
        var account = await db.PaymentProviderConfigs.AsNoTracking().Where(x => x.Id == configId)
            .Select(x => x.AccountNumber).SingleOrDefaultAsync(token);
        var accounts = await db.PaymentIntents.AsNoTracking().Where(x => x.CenterId == centerId)
            .Select(x => x.AccountNumber).Distinct().ToListAsync(token);
        var wanted = codes.ToArray();
        var intents = wanted.Length == 0
            ? []
            : await db.PaymentIntents.AsNoTracking().Where(x => x.CenterId == centerId && wanted.Contains(x.Code))
                .Select(x => new IntentRef(x.Id, x.Code, x.StudentId, x.AccountNumber)).ToListAsync(token);
        return (account, accounts, intents);
    }

    public async Task<bool> SaveShadowAsync(Guid transactionId, Guid leaseId, MatchDecision decision, DateTimeOffset now,
        CancellationToken token)
    {
        var reason = decision.Via.Length <= 200 ? decision.Via : decision.Via[..200];
        return await db.Database.ExecuteSqlRawAsync("""
            UPDATE finance.bank_transactions
            SET shadow_outcome = @outcome, shadow_reason = @reason, shadow_intent_id = @intent, shadow_student_id = @student,
                shadow_evaluated_at_utc = @now, processing_id = NULL, processing_until_utc = NULL, last_error = NULL
            WHERE id = @id AND processing_id = @lease AND status = 'Received'
            """, [
            new NpgsqlParameter("outcome", NpgsqlDbType.Varchar) { Value = decision.Outcome },
            new NpgsqlParameter("reason", NpgsqlDbType.Varchar) { Value = reason },
            new NpgsqlParameter("intent", NpgsqlDbType.Uuid) { Value = (object?)decision.IntentId ?? DBNull.Value },
            new NpgsqlParameter("student", NpgsqlDbType.Uuid) { Value = (object?)decision.StudentId ?? DBNull.Value },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() },
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = transactionId },
            new NpgsqlParameter("lease", NpgsqlDbType.Uuid) { Value = leaseId }
        ], token) == 1;
    }

    public async Task<IReadOnlyList<Guid>> ClaimAutoAsync(Guid leaseId, DateTimeOffset now, TimeSpan lease, int max,
        CancellationToken token) =>
        await db.Database.SqlQueryRaw<Guid>("""
            UPDATE finance.bank_transactions t
            SET processing_id = @lease, processing_until_utc = @until
            FROM (
                SELECT b.id FROM finance.bank_transactions b
                JOIN finance.payment_provider_configs c ON c.id = b.config_id
                WHERE b.status = 'Received' AND c.mode = 'Auto'
                  AND (b.next_attempt_at_utc IS NULL OR b.next_attempt_at_utc <= @now)
                  AND (b.processing_until_utc IS NULL OR b.processing_until_utc < @now)
                ORDER BY b.transacted_at_utc
                LIMIT @max
                FOR UPDATE OF b SKIP LOCKED) due
            WHERE t.id = due.id
            RETURNING t.id AS "Value"
            """,
            new NpgsqlParameter("lease", NpgsqlDbType.Uuid) { Value = leaseId },
            new NpgsqlParameter("until", NpgsqlDbType.TimestampTz) { Value = now.Add(lease).ToUniversalTime() },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() },
            new NpgsqlParameter("max", NpgsqlDbType.Integer) { Value = max }).ToListAsync(token);

    public async Task<IReadOnlyList<BankTransaction>> LoadTrackedAsync(IReadOnlyCollection<Guid> ids, CancellationToken token)
    {
        var wanted = ids.ToArray();
        return await db.BankTransactions.Where(x => wanted.Contains(x.Id)).ToListAsync(token);
    }

    public async Task<IReadOnlyList<PaymentIntent>> LoadIntentsTrackedAsync(Guid centerId, IReadOnlyCollection<Guid> ids,
        CancellationToken token)
    {
        var wanted = ids.ToArray();
        return wanted.Length == 0
            ? []
            : await db.PaymentIntents.Where(x => x.CenterId == centerId && wanted.Contains(x.Id)).ToListAsync(token);
    }

    public async Task FailAsync(Guid transactionId, Guid leaseId, string error, DateTimeOffset now, CancellationToken token)
    {
        var message = error.Length <= 500 ? error : error[..500];
        var attempts = await db.BankTransactions.AsNoTracking().Where(x => x.Id == transactionId)
            .Select(x => x.AttemptCount).SingleOrDefaultAsync(token);
        var next = now.Add(Backoff[Math.Min(attempts, Backoff.Length - 1)]);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.bank_transactions
            SET attempt_count = attempt_count + 1, last_error = {message}, next_attempt_at_utc = {next.ToUniversalTime()},
                processing_id = NULL, processing_until_utc = NULL,
                status = CASE WHEN attempt_count + 1 >= {MaxAttempts} THEN 'Failed' ELSE status END
            WHERE id = {transactionId} AND processing_id = {leaseId}
            """, token);
    }
}
