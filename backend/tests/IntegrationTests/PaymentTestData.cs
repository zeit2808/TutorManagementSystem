using Npgsql;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// Removes what a payment test wrote to the shared local database, even when the test fails: for each registered
/// center (always one the test created) its payment, tuition, academics and organization rows; and emails sent to the
/// registered addresses. Leftover configs would otherwise be pulled by every later fetcher run.
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
            DELETE FROM finance.payment_alerts WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_reconciliation_findings WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_reconciliation_runs WHERE center_id = ANY(@centers);
            UPDATE finance.bank_transactions SET duplicate_of_id = NULL WHERE center_id = ANY(@centers);
            DELETE FROM finance.bank_transactions WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_webhook_deliveries
              WHERE config_id IN (SELECT id FROM finance.payment_provider_configs WHERE center_id = ANY(@centers));
            DELETE FROM finance.payment_provider_configs WHERE center_id = ANY(@centers);
            DELETE FROM finance.payment_intents WHERE center_id = ANY(@centers);
            DELETE FROM finance.receipt_allocations WHERE receipt_id IN (SELECT id FROM finance.receipts WHERE center_id = ANY(@centers));
            DELETE FROM finance.receipts WHERE center_id = ANY(@centers);
            DELETE FROM finance.receipt_counters WHERE center_id = ANY(@centers);
            DELETE FROM finance.tuition_charges WHERE center_id = ANY(@centers);
            DELETE FROM finance.class_billing_anchors WHERE center_id = ANY(@centers);
            DELETE FROM finance.class_fees WHERE center_id = ANY(@centers);
            DELETE FROM finance.finance_settings WHERE center_id = ANY(@centers);
            DELETE FROM academics.enrollments WHERE center_id = ANY(@centers);
            DELETE FROM academics.teaching_sessions WHERE center_id = ANY(@centers);
            DELETE FROM academics.classes WHERE center_id = ANY(@centers);
            DELETE FROM academics.students WHERE center_id = ANY(@centers);
            DELETE FROM academics.teachers WHERE center_id = ANY(@centers);
            DELETE FROM academics.subjects WHERE center_id = ANY(@centers);
            DELETE FROM organization.rooms WHERE center_id = ANY(@centers);
            DELETE FROM organization.center_status_history WHERE center_id = ANY(@centers);
            DELETE FROM iam.center_memberships WHERE center_id = ANY(@centers);
            DELETE FROM organization.centers WHERE id = ANY(@centers);
            DELETE FROM iam.email_outbox_messages WHERE recipient = ANY(@emails);
            """, connection, transaction);
        command.Parameters.AddWithValue("centers", _centers.ToArray());
        command.Parameters.AddWithValue("emails", _emails.ToArray());
        await command.ExecuteNonQueryAsync();
        await transaction.CommitAsync();
    }
}
