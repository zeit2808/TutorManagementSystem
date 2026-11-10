using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CenterMemberDirectory(IdentityDbContext dbContext) : ICenterMemberDirectory
{
    public async Task<CenterMemberSummary?> FindActiveAsync(Guid centerId, Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await (from membership in dbContext.CenterMemberships.AsNoTracking()
                         join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                         where membership.CenterId == centerId && membership.UserId == userId &&
                               membership.IsActive && user.IsActive
                         select new { membership.UserId, membership.Role, user.Username, user.Email })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new CenterMemberSummary(row.UserId, row.Role.ToString(), row.Username ?? row.Email);
    }

    public async Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid centerId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        var wanted = roles.Select(x => Enum.TryParse<CenterMemberRole>(x, out var role) && Enum.IsDefined(role)
                ? role
                : throw new ArgumentException($"Unknown center role '{x}'.", nameof(roles)))
            .Distinct().ToArray();
        if (wanted.Length == 0) return [];
        return await (from membership in dbContext.CenterMemberships.AsNoTracking()
                      join user in dbContext.Users.AsNoTracking() on membership.UserId equals user.Id
                      where membership.CenterId == centerId && membership.IsActive && user.IsActive &&
                            wanted.Contains(membership.Role)
                      orderby membership.UserId
                      select membership.UserId)
            .Distinct().ToListAsync(cancellationToken);
    }
}
