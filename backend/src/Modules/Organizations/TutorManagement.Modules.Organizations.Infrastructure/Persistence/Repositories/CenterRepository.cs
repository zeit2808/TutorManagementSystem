using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

public sealed class CenterRepository(OrganizationDbContext dbContext) : ICenterRepository
{
    public Task<Center?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
        dbContext.Centers.SingleOrDefaultAsync(center => center.Id == centerId, cancellationToken);

    public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken) =>
        dbContext.Centers.AnyAsync(center => center.TaxCode == taxCode, cancellationToken);

    public async Task AddAsync(Center center, CancellationToken cancellationToken) =>
        await dbContext.Centers.AddAsync(center, cancellationToken);

    public Task<bool> TaxCodeExistsForOtherAsync(string taxCode, Guid exceptCenterId, CancellationToken cancellationToken) =>
        dbContext.Centers.AnyAsync(center => center.TaxCode == taxCode && center.Id != exceptCenterId, cancellationToken);

    public async Task<(IReadOnlyList<Center> Items, int TotalCount)> ListPageAsync(CenterStatus? status,
        string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Centers.AsNoTracking();
        if (status.HasValue) query = query.Where(center => center.Status == status.Value);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(center => center.TaxCode.StartsWith(term) ||
                EF.Functions.ILike(center.DisplayName, pattern, "\\") ||
                EF.Functions.ILike(center.LegalName, pattern, "\\"));
        }
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(center => center.CreatedAtUtc).ThenBy(center => center.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<CenterStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
        await dbContext.Centers.AsNoTracking().GroupBy(center => center.Status)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public async Task<IReadOnlyList<CenterStatusChange>> ListHistoryAsync(Guid centerId,
        CancellationToken cancellationToken) =>
        await dbContext.Set<CenterStatusChange>().AsNoTracking().Where(change => change.CenterId == centerId)
            .OrderBy(change => change.ChangedAtUtc).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Center>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Centers.AsNoTracking().OrderByDescending(center => center.CreatedAtUtc).ToArrayAsync(cancellationToken);
}
