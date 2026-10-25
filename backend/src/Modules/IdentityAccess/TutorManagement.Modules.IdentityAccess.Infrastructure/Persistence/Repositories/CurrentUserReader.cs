using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class CurrentUserReader(IdentityDbContext dbContext) : ICurrentUserReader
{
    private const string GoogleIssuer = "https://accounts.google.com";

    public async Task<CurrentUserDto?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking()
            .Where(x => x.Id == userId && x.IsActive)
            .Select(x => new
            {
                x.Id, x.Username, x.Email, x.EmailVerifiedAtUtc, x.PasswordHash,
                Roles = x.UserRoles.Select(role => role.Role.Name).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null) return null;

        var hasGoogle = await dbContext.ExternalLogins.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.Issuer == GoogleIssuer, cancellationToken);
        return new CurrentUserDto(user.Id, user.Username, user.Email, user.EmailVerifiedAtUtc != null,
            user.Roles.Order(StringComparer.Ordinal).ToArray(),
            !string.IsNullOrWhiteSpace(user.PasswordHash), hasGoogle);
    }
}
