namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public enum ReauthenticationResult
{
    Verified = 1,
    /// <summary>Wrong password, inactive account or temporarily blocked: the caller cannot tell which.</summary>
    Invalid = 2,
    /// <summary>The account signs in with Google only; it must set a password first.</summary>
    NoLocalPassword = 3
}

/// <summary>
/// Asks a signed-in user for their password again before a sensitive change (e.g. payment settings).
/// Failures count towards the same lockout as sign-in.
/// </summary>
public interface IPasswordReauthenticator
{
    Task<ReauthenticationResult> VerifyAsync(Guid userId, string password, string? clientIp, string? userAgent,
        string? traceId, CancellationToken cancellationToken);
}
