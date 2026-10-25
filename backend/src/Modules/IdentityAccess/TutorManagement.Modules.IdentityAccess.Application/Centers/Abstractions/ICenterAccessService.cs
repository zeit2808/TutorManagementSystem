namespace TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;

public interface ICenterAccessService
{
    Task<CenterContextDto?> FindAsync(
        Guid userId,
        Guid centerId,
        CancellationToken cancellationToken);

    Task<List<CenterContextDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
