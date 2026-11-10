using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;

public sealed record ConfirmEmailChangeCommand(Guid UserId, string NewEmail, string Otp,
    string? ClientIp, string? UserAgent, string? TraceId) : ICommand;

/// <summary>
/// Step 2: the code proves the user owns the new address. The email moves and counts as verified, the old
/// address is told about it, and any recovery code already sent to the old address stops working.
/// </summary>
public sealed class ConfirmEmailChangeCommandHandler(
    IUserRepository users,
    IUserSecurityTokenRepository tokens,
    IEmailChangeOtpService otpService,
    IEmailOutboxRepository outbox,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ConfirmEmailChangeCommand>
{
    public async Task<Result> HandleAsync(ConfirmEmailChangeCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result.Failure(CurrentUserErrors.NotFound);

        var now = clock.UtcNow;
        var newEmail = command.NewEmail.Trim();
        var normalized = RegistrationInput.NormalizeEmail(newEmail);
        var pending = await tokens.ListUnconsumedByUserAndPurposeAsync(
            user.Id, UserSecurityTokenPurposes.EmailChange, cancellationToken);
        var token = pending.SingleOrDefault(candidate => candidate.IsActive(now));
        if (token is null) return Result.Failure(IdentityErrors.InvalidEmailChangeOtp);

        if (!otpService.Verify(user.Id, token.Id, normalized, command.Otp, token.TokenHash))
        {
            token.RecordFailedAttempt(now, EmailChangePolicy.MaximumFailedAttempts);
            try { await unitOfWork.SaveChangesAsync(cancellationToken); }
            catch (IdentityConcurrencyException) { /* Another attempt changed or consumed the code. */ }
            return Result.Failure(IdentityErrors.InvalidEmailChangeOtp);
        }

        if (await users.ExistsByNormalizedEmailAsync(normalized, cancellationToken))
            return Result.Failure(IdentityErrors.EmailAlreadyExists);

        var previousEmail = user.Email;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        token.MarkUsed(now);
        user.ChangeEmail(newEmail, now);
        foreach (var purpose in new[] { UserSecurityTokenPurposes.PasswordReset, UserSecurityTokenPurposes.EmailVerification })
        {
            var stale = await tokens.ListUnconsumedByUserAndPurposeAsync(user.Id, purpose, cancellationToken);
            foreach (var item in stale) item.Revoke(now, UserSecurityTokenRevocationReasons.Superseded);
        }
        await outbox.AddAsync(ProfileMessageFactory.CreateEmailChanged(previousEmail, newEmail, now), cancellationToken);
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.EmailChanged,
            now, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (IdentityDuplicateException)
        {
            return Result.Failure(IdentityErrors.EmailAlreadyExists);
        }
        catch (IdentityConcurrencyException)
        {
            return Result.Failure(IdentityErrors.InvalidEmailChangeOtp);
        }

        return Result.Success();
    }
}
