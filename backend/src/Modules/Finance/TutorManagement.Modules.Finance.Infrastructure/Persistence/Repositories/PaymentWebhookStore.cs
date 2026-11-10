using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>Plain SQL so a webhook never loads or overwrites the config the owner may be editing at the same time.</summary>
public sealed class PaymentWebhookStore(FinanceDbContext db) : IPaymentWebhookStore
{
    public async Task<WebhookTarget?> FindByEndpointAsync(PaymentProvider provider, string endpointKey, CancellationToken token) =>
        await db.PaymentProviderConfigs.AsNoTracking()
            .Where(x => x.Provider == provider && x.EndpointKey == endpointKey)
            .Select(x => new WebhookTarget(x.Id, x.CenterId, x.WebhookSecret, x.KeyId, x.Mode))
            .SingleOrDefaultAsync(token);

    public Task RecordInvalidSignatureAsync(Guid configId, DateTimeOffset now, CancellationToken token) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_provider_configs
            SET invalid_signature_count = invalid_signature_count + 1, last_invalid_signature_at_utc = {now}
            WHERE id = {configId}
            """, token);

    public async Task RecordDeliveryAsync(Guid configId, long webhookId, string? referenceCode, long? amount, string rawBody,
        DateTimeOffset now, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO finance.payment_webhook_deliveries
                (id, config_id, webhook_id, reference_code, amount, raw_body, delivery_count, first_seen_at_utc, last_seen_at_utc)
            VALUES (@id, @config, @webhook, @reference, @amount, @body, 1, @now, @now)
            ON CONFLICT (config_id, webhook_id)
            DO UPDATE SET delivery_count = payment_webhook_deliveries.delivery_count + 1, last_seen_at_utc = EXCLUDED.last_seen_at_utc
            """, [
            new NpgsqlParameter("id", NpgsqlDbType.Uuid) { Value = id },
            new NpgsqlParameter("config", NpgsqlDbType.Uuid) { Value = configId },
            new NpgsqlParameter("webhook", NpgsqlDbType.Bigint) { Value = webhookId },
            new NpgsqlParameter("reference", NpgsqlDbType.Varchar) { Value = (object?)referenceCode ?? DBNull.Value },
            new NpgsqlParameter("amount", NpgsqlDbType.Bigint) { Value = (object?)amount ?? DBNull.Value },
            new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = rawBody },
            new NpgsqlParameter("now", NpgsqlDbType.TimestampTz) { Value = now.ToUniversalTime() }
        ], token);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE finance.payment_provider_configs
            SET pull_requested_at_utc = {now}, next_pull_at_utc = {now}
            WHERE id = {configId} AND mode <> 'Off'
            """, token);
        await transaction.CommitAsync(token);
    }
}
