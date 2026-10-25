using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class AuthSession : Entity<Guid>
{
    private AuthSession() : base(Guid.Empty) { }

    private AuthSession(Guid id, Guid userId, DateTimeOffset createdAtUtc) : base(id)
    {
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid UserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }
    public int Version { get; private set; }
    public User User { get; private set; } = null!;

    public static AuthSession Create(Guid id, Guid userId, DateTimeOffset createdAtUtc) =>
        new(id, userId, createdAtUtc);

    public bool IsActive => RevokedAtUtc is null;

    public void Touch()
    {
        if (!IsActive) throw new InvalidOperationException("The session has been revoked.");
        Version++;
    }

    public void Revoke(DateTimeOffset atUtc, string reason)
    {
        if (!IsActive) return;
        RevokedAtUtc = atUtc;
        RevocationReason = reason;
        Version++;
    }
}
