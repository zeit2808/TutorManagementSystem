using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Reauthentication;

/// <summary>
/// Checks the password of a signed-in user again. Shares the sign-in lockout, so it cannot be used to guess passwords,
/// and records failures in the security audit like a failed sign-in.
/// </summary>
public sealed class PasswordReauthenticator(
    IUserRepository users,
    ILoginSecurityRepository loginSecurity,
    IPasswordService passwords,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : IPasswordReauthenticator
{
    public async Task<ReauthenticationResult> VerifyAsync(Guid userId, string password, string? clientIp, string? userAgent,
        string? traceId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || string.IsNullOrEmpty(password) || password.Length > 256)
        {
            passwords.PerformDummyVerification(password ?? string.Empty);
            return ReauthenticationResult.Invalid;
        }

        await using var transaction = await unitOfWork.BeginLoginTransactionAsync(cancellationToken);
        var security = await loginSecurity.AcquireAsync(userId, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        var now = clock.UtcNow;
        if (user is null || !user.IsActive || security.IsBlocked(now))
        {
            passwords.PerformDummyVerification(password);
            return ReauthenticationResult.Invalid;
        }
        if (!user.HasLocalPassword)
        {
            passwords.PerformDummyVerification(password);
            return ReauthenticationResult.NoLocalPassword;
        }
        if (!passwords.Verify(user.PasswordHash!, password).Succeeded)
        {
            security.RecordFailure(now);
            await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.ReauthenticationFailed,
                now, traceId, clientIp, userAgent), cancellationToken);
            await SaveAsync(transaction, cancellationToken);
            return ReauthenticationResult.Invalid;
        }

        security.Reset();
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.Reauthenticated,
            now, traceId, clientIp, userAgent), cancellationToken);
        await SaveAsync(transaction, cancellationToken);
        return ReauthenticationResult.Verified;
    }

    private async Task SaveAsync(IIdentityTransaction transaction, CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
