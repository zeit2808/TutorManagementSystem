namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public static class RefreshTokenRevocationReasons
{
    public const string Rotated = "rotated";

    public const string Logout = "logout";

    public const string LogoutAll = "logout_all";

    public const string ReuseDetected = "reuse_detected";

    public const string PasswordChanged = "password_changed";

    public const string PasswordReset = "password_reset";
}
