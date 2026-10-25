using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;

public sealed class LogoutAllCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IAuthSessionRepository sessionRepository,
    ISecurityAuditRepository auditRepository,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LogoutAllCommand>
{
    public async Task<Result> HandleAsync(
        LogoutAllCommand command,
        CancellationToken cancellationToken)
    {
        var revokedAtUtc = clock.UtcNow;
        var sessions = await sessionRepository.ListActiveByUserIdAsync(command.UserId, cancellationToken);
        foreach (var session in sessions) session.Revoke(revokedAtUtc, RefreshTokenRevocationReasons.LogoutAll);
        var refreshTokens = await refreshTokenRepository.ListActiveByUserIdAsync(
            command.UserId,
            revokedAtUtc,
            cancellationToken);

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.Revoke(revokedAtUtc, RefreshTokenRevocationReasons.LogoutAll);
        }

        await auditRepository.AddAsync(SecurityAuditEvent.Create(
            command.UserId, SecurityAuditEventTypes.LogoutAll, revokedAtUtc), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
