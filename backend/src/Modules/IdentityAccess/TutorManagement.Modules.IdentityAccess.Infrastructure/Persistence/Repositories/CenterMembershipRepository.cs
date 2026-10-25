using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CenterMembershipRepository(IdentityDbContext dbContext)
    : ICenterMembershipRepository
{
    public Task<bool> ExistsAsync(Guid centerId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.CenterMemberships.AnyAsync(
            membership => membership.CenterId == centerId && membership.UserId == userId,
            cancellationToken);

    public async Task AddAsync(CenterMembership membership, CancellationToken cancellationToken) =>
        await dbContext.CenterMemberships.AddAsync(membership, cancellationToken);
}
