namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public static class UserSecurityTokenRevocationReasons
{
    public const string Superseded = "superseded";

    public const string EmailVerified = "email_verified";

    public const string TooManyAttempts = "too_many_attempts";
}
