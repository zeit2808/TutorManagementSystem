using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;

public sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionRepository sessionRepository,
    ITokenService tokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LogoutCommand>
{
    public async Task<Result> HandleAsync(
        LogoutCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken);
        var refreshToken = await refreshTokenRepository.FindByHashAsync(
            tokenHash,
            cancellationToken);

        if (refreshToken is not null)
        {
            var nowUtc = clock.UtcNow;
            var session = await sessionRepository.FindAsync(refreshToken.FamilyId, cancellationToken);
            if (session is { IsActive: true })
            {
                session.Revoke(nowUtc, RefreshTokenRevocationReasons.Logout);
                var family = await refreshTokenRepository.ListActiveByFamilyIdAsync(refreshToken.FamilyId, nowUtc, cancellationToken);
                foreach (var token in family) token.Revoke(nowUtc, RefreshTokenRevocationReasons.Logout);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        return Result.Success();
    }
}
