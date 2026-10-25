using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;

public sealed class ResendVerificationEmailCommandHandler(
    IUserRepository userRepository,
    IUserSecurityTokenRepository securityTokenRepository,
    IEmailOutboxRepository emailOutboxRepository,
    IOneTimeTokenService oneTimeTokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IFrontendLinks? links = null) : ICommandHandler<ResendVerificationEmailCommand>
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    public async Task<Result> HandleAsync(
        ResendVerificationEmailCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.FindByNormalizedEmailAsync(
            RegistrationInput.NormalizeEmail(command.Email),
            cancellationToken);

        if (user is null || user.IsEmailVerified)
        {
            return Result.Success();
        }

        var nowUtc = clock.UtcNow;
        try
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            if (await securityTokenRepository.WasCreatedSinceAsync(
                    user.Id, UserSecurityTokenPurposes.EmailVerification,
                    nowUtc.Subtract(ResendCooldown), cancellationToken))
                return Result.Success();

            var previousTokens = await securityTokenRepository.ListUnconsumedByUserAndPurposeAsync(
                user.Id, UserSecurityTokenPurposes.EmailVerification, cancellationToken);
            foreach (var previousToken in previousTokens)
                previousToken.Revoke(nowUtc, UserSecurityTokenRevocationReasons.Superseded);
            if (previousTokens.Count > 0) await unitOfWork.SaveChangesAsync(cancellationToken);

            var issuedToken = oneTimeTokenService.Create();
            var token = UserSecurityToken.Create(
                user.Id, UserSecurityTokenPurposes.EmailVerification,
                issuedToken.Hash, nowUtc.Add(TokenLifetime), nowUtc);
            await securityTokenRepository.AddAsync(token, cancellationToken);
            await emailOutboxRepository.AddAsync(
                EmailVerificationMessageFactory.Create(user, issuedToken.Value, token.Id, nowUtc, links?.VerifyEmailUrl(issuedToken.Value)),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (IdentityDuplicateException)
        {
            // A concurrent resend already issued the single allowed unconsumed
            // token. Keep the endpoint enumeration-safe and return success.
        }
        catch (IdentityConcurrencyException)
        {
            // A concurrent resend or verification changed the existing token.
        }

        return Result.Success();
    }
}
