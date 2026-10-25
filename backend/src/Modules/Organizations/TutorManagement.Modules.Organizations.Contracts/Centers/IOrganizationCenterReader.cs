namespace TutorManagement.Modules.Organizations.Contracts.Centers;

public sealed record CenterAccessSnapshot(
    Guid Id,
    string DisplayName,
    bool IsApproved,
    string Status = "");

public interface IOrganizationCenterReader
{
    Task<CenterAccessSnapshot?> FindAsync(Guid centerId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CenterAccessSnapshot>> ListByIdsAsync(
        IReadOnlyCollection<Guid> centerIds,
        CancellationToken cancellationToken);
}
