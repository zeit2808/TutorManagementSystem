using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed class ResetPasswordCommandHandler(
    IUserRepository users,
    IUserSecurityTokenRepository securityTokens,
    IPasswordResetOtpService otpService,
    IPasswordService passwords,
    IAuthSessionRepository sessions,
    IRefreshTokenRepository refreshTokens,
    IEmailOutboxRepository outbox,
    ILoginSecurityRepository loginSecurity,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByNormalizedEmailAsync(
            RegistrationInput.NormalizeEmail(command.Email), cancellationToken);
        if (user is null || !user.IsActive || !user.IsEmailVerified || !user.HasLocalPassword)
            return Result.Failure(IdentityErrors.InvalidPasswordResetOtp);

        var now = clock.UtcNow;
        var pending = await securityTokens.ListUnconsumedByUserAndPurposeAsync(
            user.Id, UserSecurityTokenPurposes.PasswordReset, cancellationToken);
        var token = pending.SingleOrDefault(candidate => candidate.IsActive(now));
        if (token is null) return Result.Failure(IdentityErrors.InvalidPasswordResetOtp);

        if (!otpService.Verify(user.Id, token.Id, command.Otp, token.TokenHash))
        {
            token.RecordFailedAttempt(now, PasswordResetPolicy.MaximumFailedAttempts);
            try { await unitOfWork.SaveChangesAsync(cancellationToken); }
            catch (IdentityConcurrencyException) { /* Another attempt changed or consumed the OTP. */ }
            return Result.Failure(IdentityErrors.InvalidPasswordResetOtp);
        }

        if (!PasswordPolicy.IsValid(command.NewPassword))
            return Result.Failure(IdentityErrors.WeakPassword);
        if (passwords.Verify(user.PasswordHash!, command.NewPassword).Succeeded)
            return Result.Failure(IdentityErrors.PasswordUnchanged);

        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        var security = await loginSecurity.AcquireAsync(user.Id, cancellationToken);
        security.Reset();
        token.MarkUsed(now);
        user.ChangePasswordHash(passwords.Hash(command.NewPassword));
        var allSessions = await sessions.ListActiveByUserIdAsync(user.Id, cancellationToken);
        foreach (var session in allSessions)
            session.Revoke(now, RefreshTokenRevocationReasons.PasswordReset);
        var activeRefreshTokens = await refreshTokens.ListActiveByUserIdAsync(user.Id, now, cancellationToken);
        foreach (var refresh in activeRefreshTokens)
            refresh.Revoke(now, RefreshTokenRevocationReasons.PasswordReset);
        await outbox.AddAsync(PasswordSecurityMessageFactory.CreatePasswordChanged(user, now), cancellationToken);
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.PasswordReset, now), cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (IdentityConcurrencyException)
        {
            return Result.Failure(IdentityErrors.InvalidPasswordResetOtp);
        }

        return Result.Success();
    }
}
