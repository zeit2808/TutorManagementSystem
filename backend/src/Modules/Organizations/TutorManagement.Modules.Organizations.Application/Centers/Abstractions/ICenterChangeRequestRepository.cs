using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Application.Centers.Abstractions;

public interface ICenterChangeRequestRepository
{
    Task<bool> HasPendingAsync(Guid centerId, CancellationToken cancellationToken);
    Task<CenterChangeRequest?> FindAsync(Guid centerId, Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<CenterChangeRequest>> ListAsync(Guid centerId, CancellationToken cancellationToken);
    Task AddAsync(CenterChangeRequest request, CancellationToken cancellationToken);
    Task<(IReadOnlyList<(CenterChangeRequest Request, string CenterName)> Items, int TotalCount)> ListAllAsync(
        CenterChangeStatus? status, int page, int pageSize, CancellationToken cancellationToken);
}
