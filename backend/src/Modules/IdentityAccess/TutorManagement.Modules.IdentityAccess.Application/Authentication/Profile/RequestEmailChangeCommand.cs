using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;

public sealed record RequestEmailChangeCommand(Guid UserId, string NewEmail, string CurrentPassword,
    string? ClientIp, string? UserAgent, string? TraceId) : ICommand<EmailChangeStarted>;

public sealed record EmailChangeStarted(string NewEmail, DateTimeOffset ExpiresAtUtc, DateTimeOffset ResendAvailableAtUtc);

/// <summary>
/// Step 1 of moving the sign-in email: re-checks the password (shares the sign-in lockout), then sends an
/// eight-digit code to the new address. Nothing changes until the code is confirmed.
/// </summary>
public sealed class RequestEmailChangeCommandHandler(
    IPasswordReauthenticator reauthenticator,
    IUserRepository users,
    IUserSecurityTokenRepository tokens,
    IEmailChangeOtpService otpService,
    IEmailOutboxRepository outbox,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RequestEmailChangeCommand, EmailChangeStarted>
{
    public async Task<Result<EmailChangeStarted>> HandleAsync(RequestEmailChangeCommand command,
        CancellationToken cancellationToken)
    {
        var check = await reauthenticator.VerifyAsync(command.UserId, command.CurrentPassword, command.ClientIp,
            command.UserAgent, command.TraceId, cancellationToken);
        if (check == ReauthenticationResult.NoLocalPassword)
            return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailChangeRequiresPassword);
        if (check != ReauthenticationResult.Verified)
            return Result<EmailChangeStarted>.Failure(IdentityErrors.InvalidCurrentPassword);

        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result<EmailChangeStarted>.Failure(CurrentUserErrors.NotFound);

        var newEmail = command.NewEmail.Trim();
        var normalized = RegistrationInput.NormalizeEmail(newEmail);
        if (normalized == user.NormalizedEmail) return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailUnchanged);
        if (await users.ExistsByNormalizedEmailAsync(normalized, cancellationToken))
            return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailAlreadyExists);

        var now = clock.UtcNow;
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            if (await tokens.WasCreatedSinceAsync(user.Id, UserSecurityTokenPurposes.EmailChange,
                    now - EmailChangePolicy.ResendCooldown, cancellationToken) ||
                await tokens.CountCreatedSinceAsync(user.Id, UserSecurityTokenPurposes.EmailChange,
                    now - EmailChangePolicy.SendWindow, cancellationToken) >= EmailChangePolicy.MaximumSendsPerWindow)
                return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailChangeTooFrequent);

            var previous = await tokens.ListUnconsumedByUserAndPurposeAsync(
                user.Id, UserSecurityTokenPurposes.EmailChange, cancellationToken);
            foreach (var token in previous)
                token.Revoke(now, UserSecurityTokenRevocationReasons.Superseded);
            if (previous.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);

            var tokenId = Guid.NewGuid();
            var otp = otpService.Create(user.Id, tokenId, normalized);
            var expiresAt = now + EmailChangePolicy.OtpLifetime;
            await tokens.AddAsync(UserSecurityToken.CreateWithId(tokenId, user.Id,
                UserSecurityTokenPurposes.EmailChange, otp.Hash, expiresAt, now), cancellationToken);
            await outbox.AddAsync(ProfileMessageFactory.CreateEmailChangeOtp(newEmail, tokenId, otp.Value, now),
                cancellationToken);
            await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.EmailChangeRequested,
                now, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<EmailChangeStarted>.Success(
                new EmailChangeStarted(newEmail, expiresAt, now + EmailChangePolicy.ResendCooldown));
        }
        catch (IdentityDuplicateException)
        {
            return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailChangeTooFrequent);
        }
        catch (IdentityConcurrencyException)
        {
            return Result<EmailChangeStarted>.Failure(IdentityErrors.EmailChangeTooFrequent);
        }
    }
}
