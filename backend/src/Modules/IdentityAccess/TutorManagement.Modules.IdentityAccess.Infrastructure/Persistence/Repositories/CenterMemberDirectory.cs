using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;

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
}
