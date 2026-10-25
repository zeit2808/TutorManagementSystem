using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class RefreshToken : Entity<Guid>
{
    private RefreshToken()
        : base(Guid.Empty)
    {
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? RevocationReason { get; private set; }

    public int Version { get; private set; }

    public User User { get; private set; } = null!;

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), userId, Guid.NewGuid(), tokenHash, expiresAtUtc, createdAtUtc);

    public static RefreshToken CreateReplacement(
        RefreshToken currentToken,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(currentToken);

        return new RefreshToken(
            Guid.NewGuid(),
            currentToken.UserId,
            currentToken.FamilyId,
            tokenHash,
            expiresAtUtc,
            createdAtUtc);
    }

    public bool IsActive(DateTimeOffset utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Revoke(
        DateTimeOffset revokedAtUtc,
        string reason,
        Guid? replacedByTokenId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (RevokedAtUtc is null)
        {
            RevokedAtUtc = revokedAtUtc;
            RevocationReason = reason;
            ReplacedByTokenId = replacedByTokenId;
            Version++;
        }
    }
}
