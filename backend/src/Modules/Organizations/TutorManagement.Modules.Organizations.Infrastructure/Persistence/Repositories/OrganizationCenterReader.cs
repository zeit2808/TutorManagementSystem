using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

public sealed class OrganizationCenterReader(OrganizationDbContext dbContext) : IOrganizationCenterReader
{
    public async Task<CenterAccessSnapshot?> FindAsync(Guid centerId, CancellationToken cancellationToken)
    {
        var center = await dbContext.Centers
            .AsNoTracking()
            .Where(center => center.Id == centerId)
            .Select(center => new { center.Id, center.DisplayName, center.Status })
            .SingleOrDefaultAsync(cancellationToken);
        return center is null
            ? null
            : new CenterAccessSnapshot(
                center.Id, center.DisplayName,
                center.Status == CenterStatus.Approved,
                center.Status.ToString());
    }

    public async Task<IReadOnlyCollection<CenterAccessSnapshot>> ListByIdsAsync(
        IReadOnlyCollection<Guid> centerIds,
        CancellationToken cancellationToken)
    {
        if (centerIds.Count == 0)
        {
            return Array.Empty<CenterAccessSnapshot>();
        }

        var ids = centerIds.Distinct().ToArray();
        var centers = await dbContext.Centers
            .AsNoTracking()
            .Where(center => ids.Contains(center.Id))
            .Select(center => new { center.Id, center.DisplayName, center.Status })
            .ToArrayAsync(cancellationToken);
        return centers.Select(center => new CenterAccessSnapshot(
            center.Id,
            center.DisplayName,
            center.Status == CenterStatus.Approved,
            center.Status.ToString())).ToArray();
    }
}
