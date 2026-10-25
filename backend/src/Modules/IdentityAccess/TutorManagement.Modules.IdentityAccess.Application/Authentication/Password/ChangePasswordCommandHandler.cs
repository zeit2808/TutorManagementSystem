using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed class ChangePasswordCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IAuthSessionRepository sessions,
    IUserSecurityTokenRepository securityTokens,
    IPasswordService passwords,
    ITokenService tokenService,
    IEmailOutboxRepository outbox,
    ILoginSecurityRepository loginSecurity,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ChangePasswordCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        var security = await loginSecurity.AcquireAsync(command.UserId, cancellationToken);
        var now = clock.UtcNow;
        var stored = await refreshTokens.FindByHashAsync(
            tokenService.HashRefreshToken(command.RefreshToken), cancellationToken);
        var currentSession = await sessions.FindAsync(command.SessionId, cancellationToken);
        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (stored is null || !stored.IsActive(now) || stored.UserId != command.UserId ||
            stored.FamilyId != command.SessionId || user is null || !user.IsActive ||
            currentSession is null || !currentSession.IsActive || currentSession.UserId != command.UserId)
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);

        if (!user.HasLocalPassword)
            return Result<AuthenticationResult>.Failure(IdentityErrors.PasswordNotConfigured);
        if (!passwords.Verify(user.PasswordHash!, command.CurrentPassword).Succeeded)
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCurrentPassword);
        if (!PasswordPolicy.IsValid(command.NewPassword))
            return Result<AuthenticationResult>.Failure(IdentityErrors.WeakPassword);
        if (passwords.Verify(user.PasswordHash!, command.NewPassword).Succeeded)
            return Result<AuthenticationResult>.Failure(IdentityErrors.PasswordUnchanged);

        user.ChangePasswordHash(passwords.Hash(command.NewPassword));
        security.Reset();
        var allSessions = await sessions.ListActiveByUserIdAsync(user.Id, cancellationToken);
        foreach (var session in allSessions)
        {
            if (session.Id != currentSession.Id)
                session.Revoke(now, RefreshTokenRevocationReasons.PasswordChanged);
        }

        var activeRefreshTokens = await refreshTokens.ListActiveByUserIdAsync(user.Id, now, cancellationToken);
        foreach (var token in activeRefreshTokens)
        {
            if (token.Id != stored.Id)
                token.Revoke(now, RefreshTokenRevocationReasons.PasswordChanged);
        }

        var pendingOtp = await securityTokens.ListUnconsumedByUserAndPurposeAsync(
            user.Id, UserSecurityTokenPurposes.PasswordReset, cancellationToken);
        foreach (var token in pendingOtp)
            token.Revoke(now, UserSecurityTokenRevocationReasons.Superseded);

        var issuedRefresh = tokenService.CreateRefreshToken();
        var replacement = RefreshToken.CreateReplacement(stored, issuedRefresh.Hash, issuedRefresh.ExpiresAtUtc, now);
        stored.Revoke(now, RefreshTokenRevocationReasons.Rotated, replacement.Id);
        currentSession.Touch();
        await refreshTokens.AddAsync(replacement, cancellationToken);
        await outbox.AddAsync(PasswordSecurityMessageFactory.CreatePasswordChanged(user, now), cancellationToken);
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.PasswordChanged, now), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (IdentityConcurrencyException)
        {
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidRefreshToken);
        }

        var roles = user.UserRoles.Select(role => role.Role.Name).Distinct(StringComparer.Ordinal).ToArray();
        var access = tokenService.CreateAccessToken(user, roles, currentSession.Id);
        return Result<AuthenticationResult>.Success(new AuthenticationResult(
            access.Value, issuedRefresh.Value,
            Math.Max(0, checked((int)(access.ExpiresAtUtc - clock.UtcNow).TotalSeconds)),
            access.ExpiresAtUtc, issuedRefresh.ExpiresAtUtc));
    }
}
