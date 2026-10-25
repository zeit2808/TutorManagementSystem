using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class UserSecurityToken : Entity<Guid>
{
    private UserSecurityToken()
        : base(Guid.Empty)
    {
    }

    private UserSecurityToken(
        Guid id,
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public string Purpose { get; private set; } = string.Empty;

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UsedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevocationReason { get; private set; }

    public int FailedAttemptCount { get; private set; }

    public int Version { get; private set; }

    public User User { get; private set; } = null!;

    public static UserSecurityToken Create(
        Guid userId,
        string purpose,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "The token expiration must be later than its creation time.");
        }

        return new UserSecurityToken(
            Guid.NewGuid(),
            userId,
            purpose,
            tokenHash,
            expiresAtUtc,
            createdAtUtc);
    }

    public static UserSecurityToken CreateWithId(
        Guid id, Guid userId, string purpose, string tokenHash,
        DateTimeOffset expiresAtUtc, DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Token ID is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(expiresAtUtc, createdAtUtc);
        return new UserSecurityToken(id, userId, purpose, tokenHash, expiresAtUtc, createdAtUtc);
    }

    public bool IsActive(DateTimeOffset nowUtc) =>
        UsedAtUtc is null &&
        RevokedAtUtc is null &&
        ExpiresAtUtc > nowUtc;

    public void MarkUsed(DateTimeOffset usedAtUtc)
    {
        if (UsedAtUtc is not null)
        {
            throw new InvalidOperationException("The security token has already been used.");
        }

        if (RevokedAtUtc is not null)
        {
            throw new InvalidOperationException("The security token has been revoked.");
        }

        UsedAtUtc = usedAtUtc;
        Version++;
    }

    public void RecordFailedAttempt(DateTimeOffset nowUtc, int maximumAttempts)
    {
        if (!IsActive(nowUtc)) return;
        FailedAttemptCount++;
        Version++;
        if (FailedAttemptCount >= maximumAttempts)
        {
            Revoke(nowUtc, UserSecurityTokenRevocationReasons.TooManyAttempts);
        }
    }

    public void Revoke(DateTimeOffset revokedAtUtc, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (UsedAtUtc is not null || RevokedAtUtc is not null)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        RevocationReason = reason.Trim();
        Version++;
    }
}
