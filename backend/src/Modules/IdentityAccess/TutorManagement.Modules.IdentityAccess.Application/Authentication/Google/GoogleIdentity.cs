namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? HostedDomain);

public interface IGoogleIdentityVerifier
{
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken token);
}

public interface IExternalLoginRepository
{
    Task AcquireAsync(string subject, CancellationToken token);
    Task<Guid?> FindUserIdAsync(string subject, CancellationToken token);
    Task AddAsync(Guid userId, string subject, DateTimeOffset now, CancellationToken token);
}
