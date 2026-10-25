using System.Diagnostics;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    ILoginSecurityRepository loginSecurityRepository,
    ISecurityAuditRepository auditRepository,
    IAuthSessionRepository sessionRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordService passwordService,
    ITokenService tokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<LoginCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> HandleAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var stageStarted = Stopwatch.GetTimestamp();
        void MarkStage(string stage)
        {
            var elapsed = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
            stageStarted = Stopwatch.GetTimestamp();
            Activity.Current?.SetTag($"login.stage.{stage}_ms", Math.Round(elapsed, 2));
        }
        var normalizedUsername = command.Username.Trim().ToUpperInvariant();
        var found = await userRepository.FindByNormalizedUsernameAsync(normalizedUsername, cancellationToken);
        MarkStage("lookup");

        if (found is null)
        {
            passwordService.PerformDummyVerification(command.Password);
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);
        }

        // Do the expensive hash verification before acquiring the per-user database lock.
        if (!found.HasLocalPassword)
        {
            passwordService.PerformDummyVerification(command.Password);
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);
        }
        var passwordVerification = passwordService.Verify(found.PasswordHash!, command.Password);
        MarkStage("verify");
        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        MarkStage("begin_transaction");
        var security = await loginSecurityRepository.AcquireAsync(found.Id, cancellationToken);
        MarkStage("acquire_lock_and_state");
        var user = await userRepository.FindByIdAsync(found.Id, cancellationToken)
            ?? throw new InvalidOperationException("The login user no longer exists.");
        MarkStage("reload_user_roles");
        if (user.PasswordHash != found.PasswordHash)
            passwordVerification = user.HasLocalPassword
                ? passwordService.Verify(user.PasswordHash!, command.Password)
                : new PasswordVerification(false, false);

        var now = clock.UtcNow;
        if (security.IsBlocked(now))
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);

        if (!passwordVerification.Succeeded)
        {
            var blocked = security.RecordFailure(now);
            await AuditAsync(SecurityAuditEventTypes.LoginFailed, user.Id, now, command, cancellationToken);
            if (blocked)
                await AuditAsync(SecurityAuditEventTypes.LoginTemporarilyBlocked, user.Id, now, command, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await AuditAsync(SecurityAuditEventTypes.LoginInactiveAccount, user.Id, now, command, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);
        }

        security.Reset();
        if (!user.IsEmailVerified)
        {
            await AuditAsync(SecurityAuditEventTypes.LoginUnverifiedEmail, user.Id, now, command, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<AuthenticationResult>.Failure(IdentityErrors.EmailNotVerified);
        }

        if (passwordVerification.NeedsRehash)
        {
            user.ChangePasswordHash(passwordService.Hash(command.Password));
        }

        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var refreshToken = tokenService.CreateRefreshToken();
        var storedRefreshToken = RefreshToken.Create(user.Id, refreshToken.Hash, refreshToken.ExpiresAtUtc, now);
        var accessToken = tokenService.CreateAccessToken(user, roles, storedRefreshToken.FamilyId);
        MarkStage("issue_tokens");

        await sessionRepository.AddAsync(AuthSession.Create(storedRefreshToken.FamilyId, user.Id, now), cancellationToken);
        await refreshTokenRepository.AddAsync(storedRefreshToken, cancellationToken);
        await AuditAsync(SecurityAuditEventTypes.LoginSucceeded, user.Id, now, command, cancellationToken);
        MarkStage("prepare_writes");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        MarkStage("save_changes");
        await transaction.CommitAsync(cancellationToken);
        MarkStage("commit");

        var expiresInSeconds = Math.Max(
            0,
            checked((int)(accessToken.ExpiresAtUtc - clock.UtcNow).TotalSeconds));

        return Result<AuthenticationResult>.Success(
            new AuthenticationResult(
                accessToken.Value,
                refreshToken.Value,
                expiresInSeconds,
                accessToken.ExpiresAtUtc,
                refreshToken.ExpiresAtUtc));
    }

    private Task AuditAsync(string eventType, Guid userId, DateTimeOffset occurredAtUtc,
        LoginCommand command, CancellationToken cancellationToken) =>
        auditRepository.AddAsync(SecurityAuditEvent.Create(
            userId, eventType, occurredAtUtc, command.TraceId, command.ClientIp, command.UserAgent),
            cancellationToken);
}
