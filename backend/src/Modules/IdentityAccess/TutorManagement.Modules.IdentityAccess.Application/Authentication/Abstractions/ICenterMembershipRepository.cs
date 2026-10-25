using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface ICenterMembershipRepository
{
    Task<bool> ExistsAsync(Guid centerId, Guid userId, CancellationToken cancellationToken);
    Task AddAsync(CenterMembership membership, CancellationToken cancellationToken);
}
