using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class CenterInvitation : Entity<Guid>
{
    private CenterInvitation() : base(Guid.Empty) { }

    private CenterInvitation(
        Guid id,
        Guid centerId,
        string email,
        string normalizedEmail,
        CenterMemberRole role,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        Guid invitedByUserId) : base(id)
    {
        CenterId = centerId;
        Email = email;
        NormalizedEmail = normalizedEmail;
        Role = role;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = createdAtUtc;
        InvitedByUserId = invitedByUserId;
    }

    public Guid CenterId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
    public CenterMemberRole Role { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? RevocationReason { get; private set; }

    public bool IsActiveAt(DateTimeOffset nowUtc) =>
        AcceptedAtUtc is null && RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    public static CenterInvitation CreateOwner(
        Guid centerId,
        string email,
        string normalizedEmail,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        Guid invitedByUserId) =>
        new(
            Guid.NewGuid(),
            centerId,
            email.Trim(),
            normalizedEmail,
            CenterMemberRole.CenterOwner,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            invitedByUserId);

    public static CenterInvitation CreateMember(
        Guid centerId,
        string email,
        string normalizedEmail,
        CenterMemberRole role,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc,
        Guid invitedByUserId)
    {
        if (!CenterMembership.IsAssignableByOwner(role))
        {
            throw new ArgumentException("Owners are invited by the platform.", nameof(role));
        }

        return new(
            Guid.NewGuid(),
            centerId,
            email.Trim(),
            normalizedEmail,
            role,
            tokenHash,
            expiresAtUtc,
            createdAtUtc,
            invitedByUserId);
    }

    public void Accept(Guid userId, DateTimeOffset acceptedAtUtc)
    {
        if (!IsActiveAt(acceptedAtUtc))
        {
            throw new InvalidOperationException("The invitation is not active.");
        }

        AcceptedByUserId = userId;
        AcceptedAtUtc = acceptedAtUtc;
    }

    /// <summary>
    /// Sends the same invitation again with a fresh link: the old link stops working and the expiry restarts.
    /// </summary>
    public void Reissue(string tokenHash, DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        if (AcceptedAtUtc.HasValue || RevokedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Only an open invitation can be sent again.");
        }

        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Revoke(DateTimeOffset revokedAtUtc, string reason)
    {
        if (AcceptedAtUtc.HasValue || RevokedAtUtc.HasValue)
        {
            return;
        }

        RevokedAtUtc = revokedAtUtc;
        RevocationReason = reason;
    }
}
