using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

public sealed class CenterChangeRequestRepository(OrganizationDbContext dbContext)
    : ICenterChangeRequestRepository
{
    public Task<bool> HasPendingAsync(Guid centerId, CancellationToken cancellationToken) =>
        dbContext.CenterChangeRequests.AnyAsync(
            request => request.CenterId == centerId &&
                       request.Status == CenterChangeStatus.Pending,
            cancellationToken);

    public Task<CenterChangeRequest?> FindAsync(
        Guid centerId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.CenterChangeRequests.SingleOrDefaultAsync(
            request => request.CenterId == centerId && request.Id == requestId,
            cancellationToken);

    public async Task<IReadOnlyCollection<CenterChangeRequest>> ListAsync(
        Guid centerId, CancellationToken cancellationToken) =>
        await dbContext.CenterChangeRequests.AsNoTracking()
            .Where(request => request.CenterId == centerId)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToArrayAsync(cancellationToken);

    public async Task<(IReadOnlyList<(CenterChangeRequest Request, string CenterName)> Items, int TotalCount)> ListAllAsync(
        CenterChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = from request in dbContext.CenterChangeRequests.AsNoTracking()
                    join center in dbContext.Centers.AsNoTracking() on request.CenterId equals center.Id
                    where status == null || request.Status == status
                    select new { Request = request, center.DisplayName };
        var total = await query.CountAsync(cancellationToken);
        // Oldest pending first so the queue is worked in arrival order.
        var rows = await query.OrderBy(x => x.Request.CreatedAtUtc).ThenBy(x => x.Request.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (rows.Select(x => (x.Request, x.DisplayName)).ToArray(), total);
    }

    public async Task AddAsync(CenterChangeRequest request, CancellationToken cancellationToken) =>
        await dbContext.CenterChangeRequests.AddAsync(request, cancellationToken);
}
