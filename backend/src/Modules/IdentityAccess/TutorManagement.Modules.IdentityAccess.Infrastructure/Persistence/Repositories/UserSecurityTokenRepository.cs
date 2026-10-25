using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class UserSecurityTokenRepository(IdentityDbContext dbContext)
    : IUserSecurityTokenRepository
{
    public Task<UserSecurityToken?> FindByHashAndPurposeAsync(
        string tokenHash,
        string purpose,
        CancellationToken cancellationToken) =>
        dbContext.UserSecurityTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(
                token => token.TokenHash == tokenHash && token.Purpose == purpose,
                cancellationToken);

    public async Task AddAsync(
        UserSecurityToken token,
        CancellationToken cancellationToken) =>
        await dbContext.UserSecurityTokens.AddAsync(token, cancellationToken);

    public async Task<IReadOnlyCollection<UserSecurityToken>> ListUnconsumedByUserAndPurposeAsync(
        Guid userId,
        string purpose,
        CancellationToken cancellationToken) =>
        await dbContext.UserSecurityTokens
            .Where(token =>
                token.UserId == userId &&
                token.Purpose == purpose &&
                token.UsedAtUtc == null &&
                token.RevokedAtUtc == null)
            .ToArrayAsync(cancellationToken);

    public Task<bool> WasCreatedSinceAsync(
        Guid userId,
        string purpose,
        DateTimeOffset createdSinceUtc,
        CancellationToken cancellationToken) =>
        dbContext.UserSecurityTokens.AnyAsync(
            token => token.UserId == userId &&
                     token.Purpose == purpose &&
                     token.CreatedAtUtc >= createdSinceUtc,
            cancellationToken);

    public Task<int> CountCreatedSinceAsync(Guid userId, string purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
        dbContext.UserSecurityTokens.CountAsync(token =>
            token.UserId == userId && token.Purpose == purpose && token.CreatedAtUtc >= sinceUtc,
            cancellationToken);
}
