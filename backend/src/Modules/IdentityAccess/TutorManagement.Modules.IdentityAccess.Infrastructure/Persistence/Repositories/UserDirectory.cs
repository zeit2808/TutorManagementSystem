using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class UserDirectory(IdentityDbContext dbContext) : IUserDirectory
{
    public async Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        var rows = await dbContext.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Username, x.Email }).ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.Id, x => x.Username ?? x.Email);
    }
}
