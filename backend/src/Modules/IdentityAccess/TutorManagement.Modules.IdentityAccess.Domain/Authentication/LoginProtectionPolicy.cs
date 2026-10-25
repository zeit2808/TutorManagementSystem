namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public static class LoginProtectionPolicy
{
    public static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MaximumLock = TimeSpan.FromMinutes(15);
    public const int DelayStartsAtAttempt = 5;
    public const int MaximumAttempts = 10;
}
