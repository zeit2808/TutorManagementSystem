using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;

/// <summary>
/// Rotates the refresh token. A token that was rotated moments ago and comes back again is usually not theft but
/// the same browser racing itself: two tabs refreshing together, or a page reload that dropped the response
/// carrying the new cookie. Inside <see cref="RotationGracePeriod"/> such a token is rotated once more within the
/// same session instead of revoking the whole family. Older replays are still treated as reuse.
/// </summary>
public sealed class RefreshTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionRepository sessionRepository,
    ISecurityAuditRepository auditRepository,
    ITokenService tokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<RefreshTokenCommand, AuthenticationResult>
{
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);

    public async Task<Result<AuthenticationResult>> HandleAsync(
        RefreshTokenCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken);
        var outcome = await TryRotateAsync(tokenHash, cancellationToken);
        if (outcome is not null)
        {
            return outcome;
        }

        // Another request rotated the same token between our read and our write (two tabs at once).
        // Read again: that token is now "just rotated", which the grace period answers.
        unitOfWork.DiscardChanges();
        return await TryRotateAsync(tokenHash, cancellationToken)
            ?? Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
    }

    /// <returns>The result, or <c>null</c> when a concurrent rotation won and the caller may read again once.</returns>
    private async Task<Result<AuthenticationResult>?> TryRotateAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var storedToken = await refreshTokenRepository.FindByHashWithUserAsync(tokenHash, cancellationToken);

        if (storedToken is null)
        {
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        var utcNow = clock.UtcNow;
        var session = await sessionRepository.FindAsync(storedToken.FamilyId, cancellationToken);
        if (session is null || !session.IsActive || session.UserId != storedToken.UserId)
        {
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        if (storedToken.RevokedAtUtc is not null)
        {
            if (IsWithinRotationGrace(storedToken, utcNow))
            {
                if (storedToken.ExpiresAtUtc <= utcNow || !storedToken.User.IsActive)
                {
                    return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
                }

                // Retire whatever the family holds now; the browser only keeps one cookie.
                var current = await refreshTokenRepository.ListActiveByFamilyIdAsync(
                    storedToken.FamilyId,
                    utcNow,
                    cancellationToken);
                return await IssueAsync(storedToken, current, session, utcNow, cancellationToken);
            }

            if (storedToken.ReplacedByTokenId is not null)
            {
                await RevokeFamilyForReuseAsync(storedToken, session, utcNow, cancellationToken);
            }

            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        if (!storedToken.IsActive(utcNow) || !storedToken.User.IsActive)
        {
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        return await IssueAsync(storedToken, [storedToken], session, utcNow, cancellationToken);
    }

    private static bool IsWithinRotationGrace(RefreshToken token, DateTimeOffset utcNow) =>
        token.RevocationReason == RefreshTokenRevocationReasons.Rotated &&
        token.ReplacedByTokenId is not null &&
        token.RevokedAtUtc is { } revokedAt &&
        utcNow - revokedAt <= RotationGracePeriod;

    private async Task<Result<AuthenticationResult>?> IssueAsync(
        RefreshToken presented,
        IReadOnlyList<RefreshToken> toRetire,
        AuthSession session,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var roles = presented.User.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var accessToken = tokenService.CreateAccessToken(presented.User, roles, session.Id);
        var issuedRefreshToken = tokenService.CreateRefreshToken();
        var replacementToken = RefreshToken.CreateReplacement(
            presented,
            issuedRefreshToken.Hash,
            issuedRefreshToken.ExpiresAtUtc,
            utcNow);

        foreach (var token in toRetire)
        {
            token.Revoke(utcNow, RefreshTokenRevocationReasons.Rotated, replacementToken.Id);
        }

        session.Touch();

        await refreshTokenRepository.AddAsync(
            replacementToken,
            cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityConcurrencyException)
        {
            return null;
        }

        var expiresInSeconds = Math.Max(
            0,
            checked((int)(accessToken.ExpiresAtUtc - clock.UtcNow).TotalSeconds));

        return Result<AuthenticationResult>.Success(
            new AuthenticationResult(
                accessToken.Value,
                issuedRefreshToken.Value,
                expiresInSeconds,
                accessToken.ExpiresAtUtc,
                issuedRefreshToken.ExpiresAtUtc));
    }

    private async Task RevokeFamilyForReuseAsync(
        RefreshToken storedToken,
        AuthSession session,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var activeFamilyTokens = await refreshTokenRepository.ListActiveByFamilyIdAsync(
            storedToken.FamilyId,
            utcNow,
            cancellationToken);

        foreach (var familyToken in activeFamilyTokens)
        {
            familyToken.Revoke(utcNow, RefreshTokenRevocationReasons.ReuseDetected);
        }

        session.Revoke(utcNow, RefreshTokenRevocationReasons.ReuseDetected);
        await auditRepository.AddAsync(SecurityAuditEvent.Create(
            storedToken.UserId, SecurityAuditEventTypes.RefreshReuseDetected, utcNow), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityConcurrencyException)
        {
            await sessionRepository.RevokeActiveAsync(
                storedToken.FamilyId, utcNow, RefreshTokenRevocationReasons.ReuseDetected, cancellationToken);
            await refreshTokenRepository.RevokeActiveFamilyAsync(
                storedToken.FamilyId, utcNow, RefreshTokenRevocationReasons.ReuseDetected, cancellationToken);
            await auditRepository.RecordIsolatedAsync(SecurityAuditEvent.Create(
                storedToken.UserId, SecurityAuditEventTypes.RefreshReuseDetected, utcNow), cancellationToken);
        }
    }
}
