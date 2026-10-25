using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Finance.Domain.Payroll;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Configurations;

internal static class PayrollColumns
{
    public static void Voidable<T>(EntityTypeBuilder<T> builder) where T : VoidablePayrollEntry
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.VoidedAtUtc).HasColumnName("voided_at_utc");
        builder.Property(x => x.VoidedByUserId).HasColumnName("voided_by_user_id");
        builder.Property(x => x.VoidReason).HasColumnName("void_reason").HasMaxLength(PayrollText.MaxReason);
        builder.Ignore(x => x.IsVoided);
    }
}

public sealed class PayrollConfigConfiguration : IEntityTypeConfiguration<PayrollConfig>
{
    public void Configure(EntityTypeBuilder<PayrollConfig> builder)
    {
        builder.ToTable("payroll_configs");
        builder.HasKey(x => x.CenterId);
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Enabled).HasColumnName("enabled");
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
    }
}

public sealed class PayrollConfigChangeConfiguration : IEntityTypeConfiguration<PayrollConfigChange>
{
    public void Configure(EntityTypeBuilder<PayrollConfigChange> builder)
    {
        builder.ToTable("payroll_config_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.PreviousEnabled).HasColumnName("previous_enabled");
        builder.Property(x => x.PreviousMode).HasColumnName("previous_mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Enabled).HasColumnName("enabled");
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(PayrollText.MaxReason);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => new { x.CenterId, x.CreatedAtUtc }).HasDatabaseName("ix_payroll_config_changes_center");
    }
}

public sealed class PayRateConfiguration : IEntityTypeConfiguration<PayRate>
{
    public void Configure(EntityTypeBuilder<PayRate> builder)
    {
        builder.ToTable("pay_rates", t => t.HasCheckConstraint("ck_pay_rates_amount", "amount >= 0 AND amount % 1000 = 0"));
        PayrollColumns.Voidable(builder);
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
        builder.HasIndex(x => new { x.CenterId, x.TeacherId }).HasDatabaseName("ix_pay_rates_teacher");
    }
}

public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.ToTable("payroll_adjustments", t => t.HasCheckConstraint("ck_payroll_adjustments_amount",
            "amount <> 0 AND amount % 1000 = 0"));
        PayrollColumns.Voidable(builder);
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(PayrollText.MaxReason);
        builder.HasIndex(x => new { x.CenterId, x.Month }).HasDatabaseName("ix_payroll_adjustments_month");
    }
}

public sealed class SharedExpenseConfiguration : IEntityTypeConfiguration<SharedExpense>
{
    public void Configure(EntityTypeBuilder<SharedExpense> builder)
    {
        builder.ToTable("shared_expenses", t => t.HasCheckConstraint("ck_shared_expenses_amount", "amount > 0"));
        PayrollColumns.Voidable(builder);
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.SpentOn).HasColumnName("spent_on");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(PayrollText.MaxCategory);
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(PayrollText.MaxReason);
        builder.HasIndex(x => new { x.CenterId, x.Month }).HasDatabaseName("ix_shared_expenses_month");
    }
}

public sealed class PayrollPayoutConfiguration : IEntityTypeConfiguration<PayrollPayout>
{
    public void Configure(EntityTypeBuilder<PayrollPayout> builder)
    {
        builder.ToTable("payroll_payouts", t => t.HasCheckConstraint("ck_payroll_payouts_amount", "amount > 0"));
        PayrollColumns.Voidable(builder);
        builder.Property(x => x.Number).HasColumnName("number");
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.Property(x => x.Method).HasColumnName("method").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.PaidOn).HasColumnName("paid_on");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(PayrollPayout.MaxNote);
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.HasIndex(x => new { x.CenterId, x.Number }).IsUnique().HasDatabaseName("ux_payroll_payouts_number");
        builder.HasIndex(x => new { x.CenterId, x.RequestId }).IsUnique().HasFilter("request_id IS NOT NULL")
            .HasDatabaseName("ux_payroll_payouts_request");
        builder.HasIndex(x => new { x.CenterId, x.Month }).HasDatabaseName("ix_payroll_payouts_month");
    }
}

public sealed class PayrollDisputeConfiguration : IEntityTypeConfiguration<PayrollDispute>
{
    public void Configure(EntityTypeBuilder<PayrollDispute> builder)
    {
        builder.ToTable("payroll_disputes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(PayrollText.MaxReason);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
        builder.Property(x => x.ResolvedByUserId).HasColumnName("resolved_by_user_id");
        builder.Property(x => x.Resolution).HasColumnName("resolution").HasMaxLength(PayrollText.MaxReason);
        builder.Ignore(x => x.IsOpen);
        builder.HasIndex(x => new { x.CenterId, x.Month }).HasDatabaseName("ix_payroll_disputes_month");
    }
}

public sealed class PayrollClosedMonthConfiguration : IEntityTypeConfiguration<PayrollClosedMonth>
{
    public void Configure(EntityTypeBuilder<PayrollClosedMonth> builder)
    {
        builder.ToTable("payroll_months");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ClosedByUserId).HasColumnName("closed_by_user_id");
        builder.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc");
        builder.HasIndex(x => new { x.CenterId, x.Month }).IsUnique().HasDatabaseName("ux_payroll_months_month");
        builder.OwnsMany(x => x.Lines, lines =>
        {
            lines.ToTable("payroll_month_lines");
            lines.WithOwner().HasForeignKey("MonthId");
            lines.Property<Guid>("MonthId").HasColumnName("month_id");
            lines.HasKey("MonthId", nameof(PayrollMonthLine.TeacherId));
            lines.Property(x => x.TeacherId).HasColumnName("teacher_id");
            lines.Property(x => x.Sessions).HasColumnName("sessions");
            lines.Property(x => x.Minutes).HasColumnName("minutes");
            lines.Property(x => x.Base).HasColumnName("base_amount");
            lines.Property(x => x.Adjustments).HasColumnName("adjustments");
            lines.Property(x => x.Carried).HasColumnName("carried");
            lines.Ignore(x => x.Net);
        });
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_lines");
    }
}

public sealed class PayrollCarryConfiguration : IEntityTypeConfiguration<PayrollCarry>
{
    public void Configure(EntityTypeBuilder<PayrollCarry> builder)
    {
        builder.ToTable("payroll_carries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.FromMonth).HasColumnName("from_month");
        builder.Property(x => x.IntoMonth).HasColumnName("into_month");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.Amount).HasColumnName("amount");
        builder.HasIndex(x => new { x.CenterId, x.IntoMonth }).HasDatabaseName("ix_payroll_carries_into");
    }
}

public sealed class PayrollMonthEventConfiguration : IEntityTypeConfiguration<PayrollMonthEvent>
{
    public void Configure(EntityTypeBuilder<PayrollMonthEvent> builder)
    {
        builder.ToTable("payroll_month_events");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Month).HasColumnName("month");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(PayrollText.MaxReason);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasIndex(x => new { x.CenterId, x.Month }).HasDatabaseName("ix_payroll_month_events_month");
    }
}
