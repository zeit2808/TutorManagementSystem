namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public static class SecurityAuditEventTypes
{
    public const string LoginFailed = "login_failed";
    public const string LoginTemporarilyBlocked = "login_temporarily_blocked";
    public const string LoginInactiveAccount = "login_inactive_account";
    public const string LoginUnverifiedEmail = "login_unverified_email";
    public const string LoginSucceeded = "login_succeeded";
    public const string GoogleLinked = "google_account_linked";
    public const string PasswordReset = "password_reset";
    public const string PasswordChanged = "password_changed";
    public const string LogoutAll = "logout_all";
    public const string RefreshReuseDetected = "refresh_reuse_detected";
}
