using Npgsql;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// Removes what a payment test wrote to the shared local database, even when the test fails: configs of the given
/// centers with their inbox rows, doorbells, audit events and tuition settings, and emails sent to the given addresses.
/// Leftover configs would otherwise be pulled by every later fetcher run.
/// </summary>
internal sealed class PaymentTestData(string connectionString) : IAsyncDisposable
{
    private readonly HashSet<Guid> _centers = [];
    private readonly HashSet<string> _emails = new(StringComparer.OrdinalIgnoreCase);

    public Guid Center(Guid centerId)
    {
        _centers.Add(centerId);
        return centerId;
    }

    public string Email(string address)
    {
        _emails.Add(address);
        return address;
    }

    public async ValueTask DisposeAsync()
    {
        if (_centers.Count == 0 && _emails.Count == 0) return;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM finance.payment_audit_events WHERE center_id = ANY(@centers);
            UPDATE finance.bank_transactions SET duplicate_of_id = NULL WHERE center_id = ANY(@centers);
            DELETE FROM finance.bank_transactions WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_webhook_deliveries
              WHERE config_id IN (SELECT id FROM finance.payment_provider_configs WHERE center_id = ANY(@centers));
            DELETE FROM finance.payment_provider_configs WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_intents WHERE center_id = ANY(@centers);
            DELETE FROM finance.finance_settings WHERE center_id = ANY(@centers);
            DELETE FROM iam.email_outbox_messages WHERE recipient = ANY(@emails);
            """, connection, transaction);
        command.Parameters.AddWithValue("centers", _centers.ToArray());
        command.Parameters.AddWithValue("emails", _emails.ToArray());
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
