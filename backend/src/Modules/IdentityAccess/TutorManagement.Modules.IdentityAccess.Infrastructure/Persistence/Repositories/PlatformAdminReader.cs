using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class PlatformAdminReader(IdentityDbContext dbContext) : IPlatformAdminReader
{
    public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().AnyAsync(
            user => user.Id == userId &&
                    user.IsActive &&
                    user.EmailVerifiedAtUtc != null &&
                    user.UserRoles.Any(userRole => userRole.RoleId == SystemRoles.PlatformAdminId),
            cancellationToken);
}
