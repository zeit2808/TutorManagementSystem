using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Configurations;

public sealed class PaymentProviderConfigConfiguration : IEntityTypeConfiguration<PaymentProviderConfig>
{
    public void Configure(EntityTypeBuilder<PaymentProviderConfig> builder)
    {
        builder.ToTable("payment_provider_configs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.EndpointKey).HasColumnName("endpoint_key").HasMaxLength(PaymentProviderConfig.EndpointKeyLength);
        builder.Property(x => x.WebhookSecret).HasColumnName("webhook_secret");
        builder.Property(x => x.ApiToken).HasColumnName("api_token");
        builder.Property(x => x.ApiTokenFingerprint).HasColumnName("api_token_fingerprint").HasMaxLength(64);
        builder.Property(x => x.KeyId).HasColumnName("key_id").HasMaxLength(32);
        builder.Property(x => x.BankBin).HasColumnName("bank_bin").HasMaxLength(6);
        builder.Property(x => x.AccountNumber).HasColumnName("account_number").HasMaxLength(PaymentProviderConfig.MaxAccountNumberLength);
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.AutoSinceUtc).HasColumnName("auto_since_utc");
        builder.Property(x => x.PullRequestedAtUtc).HasColumnName("pull_requested_at_utc");
        builder.Property(x => x.NextPullAtUtc).HasColumnName("next_pull_at_utc");
        builder.Property(x => x.PullRetryCount).HasColumnName("pull_retry_count");
        builder.Property(x => x.LastPulledAtUtc).HasColumnName("last_pulled_at_utc");
        builder.Property(x => x.PullCursor).HasColumnName("pull_cursor").HasMaxLength(BankTransaction.MaxIdLength);
        builder.Property(x => x.PullProcessingId).HasColumnName("pull_processing_id");
        builder.Property(x => x.PullProcessingUntilUtc).HasColumnName("pull_processing_until_utc");
        builder.Property(x => x.LastPullError).HasColumnName("last_pull_error").HasMaxLength(500);
        builder.Property(x => x.InvalidSignatureCount).HasColumnName("invalid_signature_count");
        builder.Property(x => x.LastInvalidSignatureAtUtc).HasColumnName("last_invalid_signature_at_utc");
        builder.Property(x => x.NextReconcileAtUtc).HasColumnName("next_reconcile_at_utc");
        builder.Property(x => x.ReconcileProcessingId).HasColumnName("reconcile_processing_id");
        builder.Property(x => x.ReconcileProcessingUntilUtc).HasColumnName("reconcile_processing_until_utc");
        builder.Property(x => x.ReconcileRequestedAtUtc).HasColumnName("reconcile_requested_at_utc");
        builder.Property(x => x.DigestSentOn).HasColumnName("digest_sent_on");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");

        builder.HasIndex(x => new { x.CenterId, x.Provider }).IsUnique().HasDatabaseName("ux_payment_provider_configs_center");
        builder.HasIndex(x => x.EndpointKey).IsUnique().HasDatabaseName("ux_payment_provider_configs_endpoint");
        builder.HasIndex(x => new { x.Provider, x.ApiTokenFingerprint }).IsUnique()
            .HasDatabaseName("ux_payment_provider_configs_token");
        // One bank account books automatically in one center only, so the same money never lands in two ledgers.
        builder.HasIndex(x => new { x.BankBin, x.AccountNumber }).IsUnique().HasFilter("mode = 'Auto'")
            .HasDatabaseName("ux_payment_provider_configs_auto_account");
        builder.HasIndex(x => x.NextPullAtUtc).HasFilter("mode <> 'Off'").HasDatabaseName("ix_payment_provider_configs_due");
    }
}

public sealed class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> builder)
    {
        builder.ToTable("payment_reconciliation_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ConfigId).HasColumnName("config_id");
        builder.Property(x => x.Trigger).HasColumnName("trigger").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.WindowFromUtc).HasColumnName("window_from_utc");
        builder.Property(x => x.WindowToUtc).HasColumnName("window_to_utc");
        builder.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(x => x.FinishedAtUtc).HasColumnName("finished_at_utc");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Error).HasColumnName("error").HasMaxLength(ReconciliationRun.MaxErrorLength);
        builder.Property(x => x.ScannedCount).HasColumnName("scanned_count");
        builder.Property(x => x.MissedCount).HasColumnName("missed_count");
        builder.Property(x => x.ChangedCount).HasColumnName("changed_count");
        builder.Property(x => x.IncomingTotal).HasColumnName("incoming_total");
        builder.Property(x => x.BookedTotal).HasColumnName("booked_total");
        builder.Property(x => x.BookedReceiptTotal).HasColumnName("booked_receipt_total");
        builder.Property(x => x.PendingTotal).HasColumnName("pending_total");
        builder.Property(x => x.FailedTotal).HasColumnName("failed_total");
        builder.Property(x => x.IgnoredTotal).HasColumnName("ignored_total");
        builder.Property(x => x.FindingCount).HasColumnName("finding_count");
        builder.HasIndex(x => new { x.CenterId, x.StartedAtUtc }).HasDatabaseName("ix_payment_reconciliation_runs_center");
    }
}

public sealed class ReconciliationFindingConfiguration : IEntityTypeConfiguration<ReconciliationFinding>
{
    public void Configure(EntityTypeBuilder<ReconciliationFinding> builder)
    {
        builder.ToTable("payment_reconciliation_findings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RunId).HasColumnName("run_id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(FindingKinds.MaxLength);
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id");
        builder.Property(x => x.ReceiptId).HasColumnName("receipt_id");
        builder.Property(x => x.DeliveryId).HasColumnName("delivery_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(ReconciliationFinding.MaxDetailLength);
        builder.HasIndex(x => x.RunId).HasDatabaseName("ix_payment_reconciliation_findings_run");
    }
}

public sealed class PaymentAlertConfiguration : IEntityTypeConfiguration<PaymentAlert>
{
    public void Configure(EntityTypeBuilder<PaymentAlert> builder)
    {
        builder.ToTable("payment_alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Fingerprint).HasColumnName("fingerprint").HasMaxLength(PaymentAlert.MaxFingerprintLength);
        builder.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(AlertKinds.MaxLength);
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(PaymentAlert.MaxTitleLength);
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(PaymentAlert.MaxDetailLength);
        builder.Property(x => x.State).HasColumnName("state").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Metric).HasColumnName("metric");
        builder.Property(x => x.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
        builder.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Property(x => x.LastEmailedAtUtc).HasColumnName("last_emailed_at_utc");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(x => x.AcknowledgedByUserId).HasColumnName("acknowledged_by_user_id");
        builder.Property(x => x.AcknowledgedAtUtc).HasColumnName("acknowledged_at_utc");
        builder.HasIndex(x => new { x.CenterId, x.Fingerprint }).IsUnique().HasDatabaseName("ux_payment_alerts_fingerprint");
        builder.HasIndex(x => new { x.CenterId, x.State }).HasDatabaseName("ix_payment_alerts_state");
    }
}

public sealed class PaymentWebhookDeliveryConfiguration : IEntityTypeConfiguration<PaymentWebhookDelivery>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookDelivery> builder)
    {
        builder.ToTable("payment_webhook_deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConfigId).HasColumnName("config_id");
        builder.Property(x => x.WebhookId).HasColumnName("webhook_id");
        builder.Property(x => x.ReferenceCode).HasColumnName("reference_code").HasMaxLength(PaymentWebhookDelivery.MaxReferenceLength);
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.RawBody).HasColumnName("raw_body");
        builder.Property(x => x.DeliveryCount).HasColumnName("delivery_count");
        builder.Property(x => x.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
        builder.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.HasOne<PaymentProviderConfig>().WithMany().HasForeignKey(x => x.ConfigId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ConfigId, x.WebhookId }).IsUnique().HasDatabaseName("ux_payment_webhook_deliveries_webhook");
    }
}

public sealed class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("bank_transactions", t => t.HasCheckConstraint("ck_bank_transactions_amount", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ConfigId).HasColumnName("config_id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ProviderTxnId).HasColumnName("provider_txn_id").HasMaxLength(BankTransaction.MaxIdLength);
        builder.Property(x => x.BankCode).HasColumnName("bank_code").HasMaxLength(BankTransaction.MaxBankCodeLength);
        builder.Property(x => x.BankBin).HasColumnName("bank_bin").HasMaxLength(6);
        builder.Property(x => x.AccountNumber).HasColumnName("account_number").HasMaxLength(BankTransaction.MaxAccountLength);
        builder.Property(x => x.ProviderBankAccountId).HasColumnName("provider_bank_account_id").HasMaxLength(BankTransaction.MaxIdLength);
        builder.Property(x => x.VirtualAccount).HasColumnName("virtual_account").HasMaxLength(BankTransaction.MaxAccountLength);
        builder.Property(x => x.ReferenceCode).HasColumnName("reference_code").HasMaxLength(BankTransaction.MaxReferenceLength);
        builder.Property(x => x.Direction).HasColumnName("transfer_type").HasConversion<string>().HasMaxLength(8);
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.TransactedAtUtc).HasColumnName("transacted_at_utc");
        builder.Property(x => x.RawPayload).HasColumnName("raw_payload").HasColumnType("jsonb");
        builder.Property(x => x.SemanticKey).HasColumnName("semantic_key").HasMaxLength(
            BankTransaction.MaxBankCodeLength + BankTransaction.MaxAccountLength + BankTransaction.MaxReferenceLength + 2);
        builder.Property(x => x.DuplicateOfId).HasColumnName("duplicate_of_id");
        builder.Property(x => x.PayloadChecksum).HasColumnName("payload_checksum").HasMaxLength(64);
        builder.Property(x => x.FetchedBy).HasColumnName("fetched_by").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.FirstSeenAtUtc).HasColumnName("first_seen_at_utc");
        builder.Property(x => x.LastSeenAtUtc).HasColumnName("last_seen_at_utc");
        builder.Property(x => x.SeenCount).HasColumnName("seen_count");
        builder.Property(x => x.BookedAtUtc).HasColumnName("booked_at_utc");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ReviewReason).HasColumnName("review_reason").HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.IgnoreReason).HasColumnName("ignore_reason").HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.IntentId).HasColumnName("intent_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(x => x.ProcessingId).HasColumnName("processing_id");
        builder.Property(x => x.ProcessingUntilUtc).HasColumnName("processing_until_utc");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(500);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.ShadowOutcome).HasColumnName("shadow_outcome").HasMaxLength(24);
        builder.Property(x => x.ShadowReason).HasColumnName("shadow_reason").HasMaxLength(200);
        builder.Property(x => x.ShadowIntentId).HasColumnName("shadow_intent_id");
        builder.Property(x => x.ShadowStudentId).HasColumnName("shadow_student_id");
        builder.Property(x => x.ShadowEvaluatedAtUtc).HasColumnName("shadow_evaluated_at_utc");

        builder.HasOne<PaymentProviderConfig>().WithMany().HasForeignKey(x => x.ConfigId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BankTransaction>().WithMany().HasForeignKey(x => x.DuplicateOfId).OnDelete(DeleteBehavior.Restrict);

        // Key 1: one row per provider transaction, whichever config pulled it.
        builder.HasIndex(x => new { x.Provider, x.ProviderTxnId }).IsUnique().HasDatabaseName("ux_bank_transactions_provider_txn");
        // Key 2: one row per bank reference; a second provider id for it is stored as Held without the key.
        builder.HasIndex(x => x.SemanticKey).IsUnique().HasFilter("semantic_key IS NOT NULL")
            .HasDatabaseName("ux_bank_transactions_semantic_key");
        builder.HasIndex(x => x.NextAttemptAtUtc).HasFilter("status = 'Received'").HasDatabaseName("ix_bank_transactions_pending");
        builder.HasIndex(x => new { x.CenterId, x.Status }).HasDatabaseName("ix_bank_transactions_center_status");
        builder.HasIndex(x => new { x.CenterId, x.TransactedAtUtc }).HasDatabaseName("ix_bank_transactions_center_date");
        builder.HasIndex(x => new { x.CenterId, x.Amount }).HasDatabaseName("ix_bank_transactions_center_amount");
    }
}

public sealed class PaymentAuditEventConfiguration : IEntityTypeConfiguration<PaymentAuditEvent>
{
    public void Configure(EntityTypeBuilder<PaymentAuditEvent> builder)
    {
        builder.ToTable("payment_audit_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(PaymentAuditEvent.MaxNameLength);
        builder.Property(x => x.TargetType).HasColumnName("target_type").HasMaxLength(PaymentAuditEvent.MaxNameLength);
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.Property(x => x.AtUtc).HasColumnName("at_utc");
        builder.HasIndex(x => new { x.CenterId, x.AtUtc }).HasDatabaseName("ix_payment_audit_events_center");
        // Đợt 5: the minute monitor reads provider events by action.
        builder.HasIndex(x => new { x.Action, x.AtUtc }).HasDatabaseName("ix_payment_audit_events_action");
    }
}
