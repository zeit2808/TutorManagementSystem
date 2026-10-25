using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Finance.Application.Payroll;
using TutorManagement.Modules.Finance.Domain.Payroll;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

public sealed class PayrollStore(FinanceDbContext db) : IPayrollStore
{
    public Task<PayrollConfig?> FindConfigAsync(Guid centerId, CancellationToken token) =>
        db.PayrollConfigs.SingleOrDefaultAsync(x => x.CenterId == centerId, token);

    public async Task<IReadOnlyList<PayrollConfigChange>> ListConfigChangesAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollConfigChanges.AsNoTracking().Where(x => x.CenterId == centerId).OrderBy(x => x.CreatedAtUtc)
            .ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollConfigChange>> ListAllConfigChangesAsync(CancellationToken token) =>
        await db.PayrollConfigChanges.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToArrayAsync(token);

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> LatestClosedMonthsAsync(CancellationToken token) =>
        (await db.PayrollMonths.AsNoTracking().GroupBy(x => x.CenterId)
            .Select(g => new { CenterId = g.Key, Month = g.Max(x => x.Month) }).ToListAsync(token))
        .ToDictionary(x => x.CenterId, x => x.Month);

    public async Task<IReadOnlyList<PayRate>> ListRatesAsync(Guid centerId, CancellationToken token) =>
        await db.PayRates.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollAdjustment>> ListAdjustmentsAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollAdjustments.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<SharedExpense>> ListExpensesAsync(Guid centerId, CancellationToken token) =>
        await db.SharedExpenses.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollPayout>> ListPayoutsAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollPayouts.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollDispute>> ListDisputesAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollDisputes.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollClosedMonth>> ListClosedMonthsAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollMonths.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollCarry>> ListCarriesAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollCarries.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<PayrollMonthEvent>> ListEventsAsync(Guid centerId, CancellationToken token) =>
        await db.PayrollMonthEvents.AsNoTracking().Where(x => x.CenterId == centerId).ToArrayAsync(token);

    /// <summary>C11: one counter per center, handed out atomically (same pattern as receipts).</summary>
    public async Task<int> NextPayoutNumberAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<int>(
            "INSERT INTO finance.payout_counters (center_id, last_value) VALUES (@center, 1) " +
            "ON CONFLICT (center_id) DO UPDATE SET last_value = finance.payout_counters.last_value + 1 " +
            "RETURNING last_value AS \"Value\"", new NpgsqlParameter("center", centerId)).ToListAsync(token)).Single();

    public async Task AddAsync<T>(T entity, CancellationToken token) where T : class => await db.AddAsync(entity, token);

    public void Remove<T>(T entity) where T : class => db.Remove(entity);

    public async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new PayrollPersistenceException(null, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        { throw new PayrollPersistenceException(pg.ConstraintName, ex); }
    }
}
