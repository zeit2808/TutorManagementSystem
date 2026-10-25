using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Application.Centers.Abstractions;

public interface ICenterRepository
{
    Task<Center?> FindAsync(Guid centerId, CancellationToken cancellationToken);
    Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken);
    Task AddAsync(Center center, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<Center>> ListAsync(CancellationToken cancellationToken);
    Task<bool> TaxCodeExistsForOtherAsync(string taxCode, Guid exceptCenterId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<Center> Items, int TotalCount)> ListPageAsync(CenterStatus? status, string? search,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<CenterStatus, int>> CountByStatusAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<CenterStatusChange>> ListHistoryAsync(Guid centerId, CancellationToken cancellationToken);
}
