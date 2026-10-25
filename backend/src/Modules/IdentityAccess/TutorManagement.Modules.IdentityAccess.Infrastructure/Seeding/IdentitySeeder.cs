using Microsoft.EntityFrameworkCore;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Seeding;

public sealed class IdentitySeeder(
    IdentityDbContext dbContext,
    IPasswordService passwordService,
    IClock clock)
{
    public async Task SeedOwnerAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        var normalizedUsername = username.Trim().ToUpperInvariant();
        var alreadyExists = await dbContext.Users.AnyAsync(
            user => user.NormalizedUsername == normalizedUsername,
            cancellationToken);

        if (alreadyExists)
        {
            return;
        }

        var normalizedSeedName = username.Trim().ToLowerInvariant();
        var user = User.Create(
            username,
            $"{normalizedSeedName}@local.invalid",
            "+84999999999",
            passwordService.Hash(password),
            clock.UtcNow);
        user.VerifyEmail(clock.UtcNow);
        user.AssignRole(SystemRoles.PlatformAdminId);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
