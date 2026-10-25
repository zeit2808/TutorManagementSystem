using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Finance.Domain.Payroll;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence;

public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    public DbSet<ClassFee> ClassFees => Set<ClassFee>();
    public DbSet<ClassBillingAnchor> ClassBillingAnchors => Set<ClassBillingAnchor>();
    public DbSet<TuitionCharge> TuitionCharges => Set<TuitionCharge>();
    public DbSet<TuitionDiscount> TuitionDiscounts => Set<TuitionDiscount>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<ReceiptAllocation> ReceiptAllocations => Set<ReceiptAllocation>();
    public DbSet<FinanceSettings> FinanceSettings => Set<FinanceSettings>();
    public DbSet<PayrollConfig> PayrollConfigs => Set<PayrollConfig>();
    public DbSet<PayrollConfigChange> PayrollConfigChanges => Set<PayrollConfigChange>();
    public DbSet<PayRate> PayRates => Set<PayRate>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<SharedExpense> SharedExpenses => Set<SharedExpense>();
    public DbSet<PayrollPayout> PayrollPayouts => Set<PayrollPayout>();
    public DbSet<PayrollDispute> PayrollDisputes => Set<PayrollDispute>();
    public DbSet<PayrollClosedMonth> PayrollMonths => Set<PayrollClosedMonth>();
    public DbSet<PayrollCarry> PayrollCarries => Set<PayrollCarry>();
    public DbSet<PayrollMonthEvent> PayrollMonthEvents => Set<PayrollMonthEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FinanceDbContext).Assembly);
    }
}
