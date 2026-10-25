using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed class ForgotPasswordCommandHandler(
    IUserRepository users,
    IUserSecurityTokenRepository tokens,
    IPasswordResetOtpService otpService,
    IEmailOutboxRepository outbox,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ForgotPasswordCommand>
{
    public async Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByNormalizedEmailAsync(
            RegistrationInput.NormalizeEmail(command.Email), cancellationToken);
        if (user is null || !user.IsActive || !user.IsEmailVerified || !user.HasLocalPassword) return Result.Success();

        var now = clock.UtcNow;
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            if (await tokens.CountCreatedSinceAsync(
                    user.Id, UserSecurityTokenPurposes.PasswordReset, now - PasswordResetPolicy.SendWindow, cancellationToken)
                >= PasswordResetPolicy.MaximumSendsPerWindow ||
                await tokens.WasCreatedSinceAsync(
                    user.Id, UserSecurityTokenPurposes.PasswordReset, now - PasswordResetPolicy.ResendCooldown, cancellationToken))
            {
                return Result.Success();
            }

            var previous = await tokens.ListUnconsumedByUserAndPurposeAsync(
                user.Id, UserSecurityTokenPurposes.PasswordReset, cancellationToken);
            foreach (var token in previous)
                token.Revoke(now, UserSecurityTokenRevocationReasons.Superseded);
            if (previous.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);

            var tokenId = Guid.NewGuid();
            var otp = otpService.Create(user.Id, tokenId);
            await tokens.AddAsync(UserSecurityToken.CreateWithId(
                tokenId, user.Id, UserSecurityTokenPurposes.PasswordReset, otp.Hash,
                now + PasswordResetPolicy.OtpLifetime, now), cancellationToken);
            await outbox.AddAsync(PasswordSecurityMessageFactory.CreateResetOtp(user, tokenId, otp.Value, now), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (IdentityDuplicateException) { /* Concurrent request won the unique active-token index. */ }
        catch (IdentityConcurrencyException) { /* Concurrent resend superseded this request. */ }
        return Result.Success();
    }
}
