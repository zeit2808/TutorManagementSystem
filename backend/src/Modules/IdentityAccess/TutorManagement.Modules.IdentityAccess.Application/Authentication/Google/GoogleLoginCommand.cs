using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;

public sealed record GoogleLoginCommand(string IdToken, string? ClientIp, string? UserAgent, string? TraceId)
    : ICommand<AuthenticationResult>;

public sealed class GoogleLoginCommandHandler(
    IGoogleIdentityVerifier verifier,
    IExternalLoginRepository externalLogins,
    IUserRepository users,
    IAuthSessionRepository sessions,
    IRefreshTokenRepository refreshTokens,
    ISecurityAuditRepository audit,
    ITokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<GoogleLoginCommand, AuthenticationResult>
{
    public async Task<Result<AuthenticationResult>> HandleAsync(GoogleLoginCommand command, CancellationToken cancellationToken)
    {
        var google = await verifier.VerifyAsync(command.IdToken, cancellationToken);
        if (google is null || !google.EmailVerified || string.IsNullOrWhiteSpace(google.Email) ||
            google.Email.Length > 254 || google.Subject.Length > 255)
            return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidGoogleToken);

        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        await externalLogins.AcquireAsync(google.Subject, cancellationToken);
        var existingId = await externalLogins.FindUserIdAsync(google.Subject, cancellationToken);
        User user;
        if (existingId.HasValue)
        {
            user = await users.FindByIdAsync(existingId.Value, cancellationToken)
                ?? throw new InvalidOperationException("External login references a missing user.");
            if (!user.IsActive)
                return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials);
        }
        else
        {
            // Email equality does not prove that two provider accounts belong to the same person.
            if (await users.ExistsByNormalizedEmailAsync(google.Email.Trim().ToUpperInvariant(), cancellationToken))
                return Result<AuthenticationResult>.Failure(IdentityErrors.GoogleAccountLinkRequired);
            user = User.CreateExternal(google.Email, clock.UtcNow);
            // A third-party mailbox verified by Google can later change hands.
            // Only Gmail and managed Workspace domains are authoritative mailboxes.
            if (google.Email.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(google.HostedDomain))
                user.VerifyEmail(clock.UtcNow);
            user.AssignRole(SystemRoles.StudentId);
            await users.AddAsync(user, cancellationToken);
            await externalLogins.AddAsync(user.Id, google.Subject, clock.UtcNow, cancellationToken);
        }

        var now = clock.UtcNow;
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.Create(user.Id, refresh.Hash, refresh.ExpiresAtUtc, now);
        var roles = existingId.HasValue
            ? user.UserRoles.Select(x => x.Role.Name).Distinct(StringComparer.Ordinal).ToArray()
            : [SystemRoles.Student];
        var access = tokens.CreateAccessToken(user, roles, stored.FamilyId);
        await sessions.AddAsync(AuthSession.Create(stored.FamilyId, user.Id, now), cancellationToken);
        await refreshTokens.AddAsync(stored, cancellationToken);
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.LoginSucceeded,
            now, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
        try { await unitOfWork.SaveChangesAsync(cancellationToken); }
        catch (IdentityDuplicateException ex) when (ex.ConstraintName == "ux_users_normalized_email")
        { return Result<AuthenticationResult>.Failure(IdentityErrors.GoogleAccountLinkRequired); }
        catch (IdentityDuplicateException)
        { return Result<AuthenticationResult>.Failure(IdentityErrors.GoogleAccountLinkRequired); }
        catch (IdentityConcurrencyException)
        { return Result<AuthenticationResult>.Failure(IdentityErrors.InvalidCredentials); }
        await transaction.CommitAsync(cancellationToken);

        return Result<AuthenticationResult>.Success(new AuthenticationResult(
            access.Value, refresh.Value,
            Math.Max(0, checked((int)(access.ExpiresAtUtc - clock.UtcNow).TotalSeconds)),
            access.ExpiresAtUtc, refresh.ExpiresAtUtc));
    }
}
