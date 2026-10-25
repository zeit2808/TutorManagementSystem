namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public static class PasswordResetPolicy
{
    public static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(24);
    public const int MaximumSendsPerWindow = 5;
    public const int MaximumFailedAttempts = 5;
}
