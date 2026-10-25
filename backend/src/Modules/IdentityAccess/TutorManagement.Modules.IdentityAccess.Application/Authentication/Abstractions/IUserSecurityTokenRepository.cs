using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IUserSecurityTokenRepository
{
    Task<UserSecurityToken?> FindByHashAndPurposeAsync(
        string tokenHash,
        string purpose,
        CancellationToken cancellationToken);

    Task AddAsync(UserSecurityToken token, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<UserSecurityToken>> ListUnconsumedByUserAndPurposeAsync(
        Guid userId,
        string purpose,
        CancellationToken cancellationToken);

    Task<bool> WasCreatedSinceAsync(
        Guid userId,
        string purpose,
        DateTimeOffset createdSinceUtc,
        CancellationToken cancellationToken);

    Task<int> CountCreatedSinceAsync(Guid userId, string purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken);
}
