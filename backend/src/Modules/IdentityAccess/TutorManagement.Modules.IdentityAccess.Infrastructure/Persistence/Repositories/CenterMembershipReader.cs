using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CenterMembershipReader(IdentityDbContext dbContext)
    : ICenterMembershipReader
{
    public Task<CenterMembershipSnapshot?> FindActiveAsync(
        Guid userId,
        Guid centerId,
        CancellationToken cancellationToken) =>
        dbContext.CenterMemberships
            .AsNoTracking()
            .Where(membership =>
                membership.UserId == userId &&
                membership.CenterId == centerId &&
                membership.IsActive)
            .Join(
                dbContext.Users.AsNoTracking().Where(user =>
                    user.IsActive && user.EmailVerifiedAtUtc != null),
                membership => membership.UserId,
                user => user.Id,
                (membership, _) => new CenterMembershipSnapshot(
                    membership.CenterId,
                    membership.Role))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<CenterMembershipSnapshot>> ListActiveAsync(
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.CenterMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == userId && membership.IsActive)
            .Join(
                dbContext.Users.AsNoTracking().Where(user =>
                    user.IsActive && user.EmailVerifiedAtUtc != null),
                membership => membership.UserId,
                user => user.Id,
                (membership, _) => new CenterMembershipSnapshot(
                    membership.CenterId,
                    membership.Role))
            .ToArrayAsync(cancellationToken);
}
