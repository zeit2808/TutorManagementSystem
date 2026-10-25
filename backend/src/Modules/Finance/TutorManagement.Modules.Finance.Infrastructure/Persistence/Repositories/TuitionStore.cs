using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

public sealed class TuitionStore(FinanceDbContext db, IClock clock, IServiceProvider services) : ITuitionStore
{
    public string? StudentPortalUrl { get; } = Normalize(services.GetService<IConfiguration>()?["Frontend:BaseUrl"]);

    public async Task<ITuitionGuard> BeginCenterGuardAsync(Guid centerId, CancellationToken token)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var key = $"finance:{centerId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new Guard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<ClassFee>> ListFeesAsync(Guid centerId, CancellationToken token) =>
        await db.ClassFees.AsNoTracking().Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<ClassBillingAnchor>> ListAnchorsAsync(Guid centerId, CancellationToken token) =>
        await db.ClassBillingAnchors.AsNoTracking().Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<TuitionCharge>> ListChargesAsync(Guid centerId, CancellationToken token) =>
        await db.TuitionCharges.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<TuitionDiscount>> ListDiscountsAsync(Guid centerId, CancellationToken token) =>
        await db.TuitionDiscounts.Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public async Task<IReadOnlyList<Receipt>> ListReceiptsAsync(Guid centerId, CancellationToken token) =>
        await db.Receipts.Include(x => x.Allocations).Where(x => x.CenterId == centerId).ToArrayAsync(token);

    public Task<FinanceSettings?> FindSettingsAsync(Guid centerId, CancellationToken token) =>
        db.FinanceSettings.SingleOrDefaultAsync(x => x.CenterId == centerId, token);

    /// <summary>H12: one counter per center, handed out atomically.</summary>
    public async Task<int> NextReceiptNumberAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<int>(
            "INSERT INTO finance.receipt_counters (center_id, last_value) VALUES (@center, 1) " +
            "ON CONFLICT (center_id) DO UPDATE SET last_value = finance.receipt_counters.last_value + 1 " +
            "RETURNING last_value AS \"Value\"", new NpgsqlParameter("center", centerId)).ToListAsync(token)).Single();

    public async Task<IReadOnlyList<Guid>> ListCentersWithFeesAsync(CancellationToken token) =>
        await db.ClassFees.AsNoTracking().Select(x => x.CenterId).Distinct().ToArrayAsync(token);

    public async Task AddAsync<T>(T entity, CancellationToken token) where T : class => await db.AddAsync(entity, token);

    public async Task SaveAsync(CancellationToken token)
    {
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException ex) { throw new TuitionPersistenceException(null, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        { throw new TuitionPersistenceException(pg.ConstraintName, ex); }
    }

    /// <summary>The outbox belongs to IdentityAccess; writing it here keeps the email inside this transaction.</summary>
    public async Task QueueEmailsAsync(IReadOnlyList<OutgoingTuitionEmail> emails, CancellationToken token)
    {
        var now = clock.UtcNow;
        foreach (var email in emails)
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO iam.email_outbox_messages (id, recipient, subject, body, created_at_utc, attempt_count)
                VALUES ({id}, {email.Recipient}, {email.Subject}, {email.Body}, {now}, 0)
                """, token);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ReadEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        var ids = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        var rows = await db.Database.SqlQueryRaw<IdText>(
            "SELECT id AS \"Id\", email AS \"Text\" FROM iam.users WHERE id = ANY(@ids) AND is_active AND email IS NOT NULL",
            new NpgsqlParameter("ids", ids)).ToListAsync(token);
        return rows.ToDictionary(x => x.Id, x => x.Text);
    }

    private static string? Normalize(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/student"
            : null;

    public sealed record IdText(Guid Id, string Text);

    private sealed class Guard(IDbContextTransaction transaction) : ITuitionGuard
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
