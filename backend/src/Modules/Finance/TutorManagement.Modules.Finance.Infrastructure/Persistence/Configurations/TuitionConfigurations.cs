using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Configurations;

public sealed class ClassFeeConfiguration : IEntityTypeConfiguration<ClassFee>
{
    public void Configure(EntityTypeBuilder<ClassFee> builder)
    {
        builder.ToTable("class_fees", t => t.HasCheckConstraint("ck_class_fees_amount",
            "monthly_fee >= 0 AND monthly_fee % 1000 = 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.MonthlyFee).HasColumnName("monthly_fee");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => new { x.CenterId, x.ClassId, x.EffectiveFrom }).HasDatabaseName("ix_class_fees_class");
    }
}

public sealed class ClassBillingAnchorConfiguration : IEntityTypeConfiguration<ClassBillingAnchor>
{
    public void Configure(EntityTypeBuilder<ClassBillingAnchor> builder)
    {
        builder.ToTable("class_billing_anchors");
        builder.HasKey(x => new { x.CenterId, x.ClassId });
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.AnchorDate).HasColumnName("anchor_date");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
    }
}

public sealed class TuitionChargeConfiguration : IEntityTypeConfiguration<TuitionCharge>
{
    public void Configure(EntityTypeBuilder<TuitionCharge> builder)
    {
        builder.ToTable("tuition_charges", t =>
        {
            t.HasCheckConstraint("ck_tuition_charges_amounts",
                "amount >= 0 AND list_amount >= 0 AND discount_amount >= 0 AND adjustment_amount >= 0 AND waived_amount >= 0");
            t.HasCheckConstraint("ck_tuition_charges_period", "period_end >= period_start");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(x => x.CycleIndex).HasColumnName("cycle_index");
        builder.Property(x => x.PeriodStart).HasColumnName("period_start");
        builder.Property(x => x.PeriodEnd).HasColumnName("period_end");
        builder.Property(x => x.MonthlyFee).HasColumnName("monthly_fee");
        builder.Property(x => x.JoinSessions).HasColumnName("join_sessions");
        builder.Property(x => x.PeriodSessions).HasColumnName("period_sessions");
        builder.Property(x => x.ListAmount).HasColumnName("list_amount");
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.EndedOn).HasColumnName("ended_on");
        builder.Property(x => x.EndedSessionsAttended).HasColumnName("ended_sessions_attended");
        builder.Property(x => x.EndedSessionsTotal).HasColumnName("ended_sessions_total");
        builder.Property(x => x.EndedValue).HasColumnName("ended_value");
        builder.Property(x => x.WaivedAmount).HasColumnName("waived_amount");
        builder.Property(x => x.RemindedAtUtc).HasColumnName("reminded_at_utc");
        builder.Property(x => x.AdjustmentAmount).HasColumnName("adjustment_amount");
        builder.Property(x => x.AdjustmentSessions).HasColumnName("adjustment_sessions");
        builder.Property(x => x.AdjustmentReason).HasColumnName("adjustment_reason").HasMaxLength(TuitionCharge.MaxReasonLength);
        builder.Property(x => x.AdjustedAtUtc).HasColumnName("adjusted_at_utc");
        builder.Property(x => x.AdjustedByUserId).HasColumnName("adjusted_by_user_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.JoinedMidCycle);
        builder.HasIndex(x => new { x.EnrollmentId, x.CycleIndex }).IsUnique().HasDatabaseName("ux_tuition_charges_enrollment_cycle");
        builder.HasIndex(x => new { x.CenterId, x.StudentId, x.PeriodStart }).HasDatabaseName("ix_tuition_charges_student");
        builder.HasIndex(x => new { x.CenterId, x.ClassId, x.PeriodStart }).HasDatabaseName("ix_tuition_charges_class");
    }
}

public sealed class TuitionDiscountConfiguration : IEntityTypeConfiguration<TuitionDiscount>
{
    public void Configure(EntityTypeBuilder<TuitionDiscount> builder)
    {
        builder.ToTable("tuition_discounts", t => t.HasCheckConstraint("ck_tuition_discounts_value",
            "(kind = 'Percent' AND value BETWEEN 1 AND 100) OR (kind = 'Amount' AND value > 0 AND value % 1000 = 0)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Value).HasColumnName("value");
        builder.Property(x => x.FromDate).HasColumnName("from_date");
        builder.Property(x => x.ToDate).HasColumnName("to_date");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(TuitionDiscount.MaxReasonLength).IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(x => x.RevokedByUserId).HasColumnName("revoked_by_user_id");
        builder.HasIndex(x => new { x.CenterId, x.StudentId, x.ClassId }).HasDatabaseName("ix_tuition_discounts_student");
    }
}

public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("receipts", t => t.HasCheckConstraint("ck_receipts_amount", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Number).HasColumnName("number");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.PayerName).HasColumnName("payer_name").HasMaxLength(Receipt.MaxPayerLength);
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.ReceivedOn).HasColumnName("received_on");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(Receipt.MaxNoteLength);
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc");
        builder.Property(x => x.VoidedByUserId).HasColumnName("voided_by_user_id");
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(Receipt.MaxNoteLength);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.IsVoided);
        builder.Ignore(x => x.Allocated);
        builder.Ignore(x => x.Unallocated);
        builder.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade).HasConstraintName("fk_receipt_allocations_receipt");
        builder.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_allocations");
        builder.HasIndex(x => new { x.CenterId, x.Number }).IsUnique().HasDatabaseName("ux_receipts_number");
        builder.HasIndex(x => new { x.CenterId, x.RequestId }).IsUnique().HasFilter("request_id IS NOT NULL")
            .HasDatabaseName("ux_receipts_request");
        builder.HasIndex(x => new { x.CenterId, x.StudentId }).HasDatabaseName("ix_receipts_student");
    }
}

public sealed class PaymentIntentConfiguration : IEntityTypeConfiguration<PaymentIntent>
{
    public void Configure(EntityTypeBuilder<PaymentIntent> builder)
    {
        builder.ToTable("payment_intents", t => t.HasCheckConstraint("ck_payment_intents_amount", "amount > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(PaymentCode.Length);
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.ChargeIds).HasColumnName("charge_ids");
        builder.Property(x => x.BankBin).HasColumnName("bank_bin").HasMaxLength(6);
        builder.Property(x => x.AccountNumber).HasColumnName("account_number").HasMaxLength(20);
        builder.Property(x => x.AccountName).HasColumnName("account_name").HasMaxLength(100);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Ignore(x => x.TransferContent);
        builder.Ignore(x => x.QrPayload);
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("ux_payment_intents_code");
        builder.HasIndex(x => new { x.CenterId, x.StudentId }).HasFilter("status = 'Pending'")
            .HasDatabaseName("ix_payment_intents_pending");
    }
}

public sealed class ReceiptAllocationConfiguration : IEntityTypeConfiguration<ReceiptAllocation>
{
    public void Configure(EntityTypeBuilder<ReceiptAllocation> builder)
    {
        builder.ToTable("receipt_allocations", t => t.HasCheckConstraint("ck_receipt_allocations_amount", "amount > 0"));
        builder.HasKey(x => x.Id);
        // The id is made in the domain. Without this, an allocation added to an already saved receipt (credit spread
        // onto a newly billed cycle) is taken for an existing row, updated, and fails as a concurrency conflict.
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ReceiptId).HasColumnName("receipt_id");
        builder.Property(x => x.ChargeId).HasColumnName("charge_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasOne<TuitionCharge>().WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_receipt_allocations_charge");
        builder.HasIndex(x => new { x.ReceiptId, x.ChargeId }).IsUnique().HasDatabaseName("ux_receipt_allocations_line");
        builder.HasIndex(x => x.ChargeId).HasDatabaseName("ix_receipt_allocations_charge");
    }
}

public sealed class FinanceSettingsConfiguration : IEntityTypeConfiguration<FinanceSettings>
{
    public void Configure(EntityTypeBuilder<FinanceSettings> builder)
    {
        builder.ToTable("finance_settings", t => t.HasCheckConstraint("ck_finance_settings_due_days",
            "due_days BETWEEN 0 AND 60"));
        builder.HasKey(x => x.CenterId);
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.BankBin).HasColumnName("bank_bin").HasMaxLength(6);
        builder.Property(x => x.BankAccountNumber).HasColumnName("bank_account_number").HasMaxLength(20);
        builder.Property(x => x.BankAccountName).HasColumnName("bank_account_name").HasMaxLength(100);
        builder.Property(x => x.DueDays).HasColumnName("due_days");
        builder.Property(x => x.EmailReminders).HasColumnName("email_reminders");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.HasBankAccount);
    }
}
