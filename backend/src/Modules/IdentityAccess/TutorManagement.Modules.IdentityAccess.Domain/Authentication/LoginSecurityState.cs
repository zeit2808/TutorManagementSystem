namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

// One row per known account. The repository locks the owning user row before loading it.
public sealed class LoginSecurityState
{
    private LoginSecurityState() { }

    private LoginSecurityState(Guid userId) => UserId = userId;

    public Guid UserId { get; private set; }
    public int FailedAttemptCount { get; private set; }
    public DateTimeOffset? WindowStartedAtUtc { get; private set; }
    public DateTimeOffset? BlockedUntilUtc { get; private set; }
    public int Version { get; private set; }
    public User User { get; private set; } = null!;

    public static LoginSecurityState Create(Guid userId) => new(userId);

    public bool IsBlocked(DateTimeOffset nowUtc) => BlockedUntilUtc > nowUtc;

    public bool RecordFailure(DateTimeOffset nowUtc)
    {
        if (WindowStartedAtUtc is null || nowUtc - WindowStartedAtUtc >= LoginProtectionPolicy.AttemptWindow)
        {
            WindowStartedAtUtc = nowUtc;
            FailedAttemptCount = 0;
        }

        FailedAttemptCount++;
        var previousBlock = BlockedUntilUtc;
        // Progressive backoff precedes a bounded lockout, limiting attacker-induced denial of service.
        BlockedUntilUtc = FailedAttemptCount switch
        {
            >= LoginProtectionPolicy.MaximumAttempts => nowUtc + LoginProtectionPolicy.MaximumLock,
            >= LoginProtectionPolicy.DelayStartsAtAttempt => nowUtc +
                LoginProtectionPolicy.InitialDelay *
                Math.Min(FailedAttemptCount - LoginProtectionPolicy.DelayStartsAtAttempt + 1, 5),
            _ => null
        };
        Version++;
        return BlockedUntilUtc is not null && BlockedUntilUtc != previousBlock;
    }

    public void Reset()
    {
        if (FailedAttemptCount == 0 && BlockedUntilUtc is null) return;
        FailedAttemptCount = 0;
        WindowStartedAtUtc = null;
        BlockedUntilUtc = null;
        Version++;
    }
}
