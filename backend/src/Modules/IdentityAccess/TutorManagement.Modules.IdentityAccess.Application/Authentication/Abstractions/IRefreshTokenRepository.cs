using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task<RefreshToken?> FindByHashWithUserAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(
        Guid userId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> ListActiveByFamilyIdAsync(
        Guid familyId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<int> RevokeActiveFamilyAsync(
        Guid familyId,
        DateTimeOffset revokedAtUtc,
        string reason,
        CancellationToken cancellationToken);
}
