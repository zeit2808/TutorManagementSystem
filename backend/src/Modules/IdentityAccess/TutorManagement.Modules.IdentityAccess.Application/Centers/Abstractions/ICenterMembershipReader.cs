using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;

public sealed record CenterMembershipSnapshot(
    Guid CenterId,
    CenterMemberRole Role);

public interface ICenterMembershipReader
{
    Task<CenterMembershipSnapshot?> FindActiveAsync(
        Guid userId,
        Guid centerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<CenterMembershipSnapshot>> ListActiveAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
