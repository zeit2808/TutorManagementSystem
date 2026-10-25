using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.VerifyEmail;

public sealed class VerifyEmailCommandHandler(
    IUserSecurityTokenRepository securityTokenRepository,
    IOneTimeTokenService oneTimeTokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Result> HandleAsync(
        VerifyEmailCommand command,
        CancellationToken cancellationToken)
    {
        var tokenHash = oneTimeTokenService.Hash(command.Token);
        var token = await securityTokenRepository.FindByHashAndPurposeAsync(
            tokenHash,
            UserSecurityTokenPurposes.EmailVerification,
            cancellationToken);

        var nowUtc = clock.UtcNow;
        if (token is null || !token.IsActive(nowUtc))
        {
            return Result.Failure(IdentityErrors.InvalidEmailVerificationToken);
        }

        token.User.VerifyEmail(nowUtc);
        token.MarkUsed(nowUtc);

        var otherTokens = await securityTokenRepository
            .ListUnconsumedByUserAndPurposeAsync(
                token.UserId,
                UserSecurityTokenPurposes.EmailVerification,
                cancellationToken);
        foreach (var otherToken in otherTokens)
        {
            if (otherToken.Id != token.Id)
            {
                otherToken.Revoke(
                    nowUtc,
                    UserSecurityTokenRevocationReasons.EmailVerified);
            }
        }

        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (IdentityConcurrencyException)
        {
            return Result.Failure(IdentityErrors.InvalidEmailVerificationToken);
        }

        return Result.Success();
    }
}
