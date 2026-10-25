using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;

public sealed record LinkGoogleCommand(Guid UserId, string IdToken, string CurrentPassword, string? ClientIp,
    string? UserAgent, string? TraceId) : ICommand;

public sealed class LinkGoogleCommandHandler(
    IGoogleIdentityVerifier verifier,
    IExternalLoginRepository externalLogins,
    IUserRepository users,
    ILoginSecurityRepository loginSecurity,
    IPasswordService passwords,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LinkGoogleCommand>
{
    public async Task<Result> HandleAsync(LinkGoogleCommand command, CancellationToken cancellationToken)
    {
        var google = await verifier.VerifyAsync(command.IdToken, cancellationToken);
        if (google is null || !google.EmailVerified || string.IsNullOrWhiteSpace(google.Email) ||
            google.Subject.Length > 255)
            return Result.Failure(IdentityErrors.InvalidGoogleToken);

        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        await externalLogins.AcquireAsync(google.Subject, cancellationToken);
        var linkedUserId = await externalLogins.FindUserIdAsync(google.Subject, cancellationToken);
        if (linkedUserId.HasValue && linkedUserId.Value != command.UserId)
            return Result.Failure(IdentityErrors.GoogleAlreadyLinked);
        var security = await loginSecurity.AcquireAsync(command.UserId, cancellationToken);
        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result.Failure(IdentityErrors.InvalidCredentials);
        var now = clock.UtcNow;
        if (security.IsBlocked(now)) return Result.Failure(IdentityErrors.InvalidCredentials);
        if (!user.HasLocalPassword || !passwords.Verify(user.PasswordHash!, command.CurrentPassword).Succeeded)
        {
            security.RecordFailure(now);
            await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.LoginFailed,
                now, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result.Failure(IdentityErrors.InvalidCredentials);
        }
        security.Reset();
        if (!linkedUserId.HasValue)
        {
            await externalLogins.AddAsync(user.Id, google.Subject, clock.UtcNow, cancellationToken);
            await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.GoogleLinked,
                clock.UtcNow, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
        }
        // Linking adds a way to sign in to this staff account; it never changes what the account is.
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (IdentityDuplicateException)
        { return Result.Failure(IdentityErrors.GoogleAlreadyLinked); }
        catch (IdentityConcurrencyException)
        { return Result.Failure(IdentityErrors.InvalidCredentials); }
        await transaction.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
