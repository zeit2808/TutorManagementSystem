using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class CenterMembership : Entity<Guid>
{
    private CenterMembership() : base(Guid.Empty) { }

    private CenterMembership(
        Guid id,
        Guid centerId,
        Guid userId,
        CenterMemberRole role,
        DateTimeOffset createdAtUtc) : base(id)
    {
        CenterId = centerId;
        UserId = userId;
        Role = role;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid CenterId { get; private set; }
    public Guid UserId { get; private set; }
    public CenterMemberRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static CenterMembership CreateOwner(
        Guid centerId,
        Guid userId,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), centerId, userId, CenterMemberRole.CenterOwner, createdAtUtc);

    public static CenterMembership Create(
        Guid centerId,
        Guid userId,
        CenterMemberRole role,
        DateTimeOffset createdAtUtc)
    {
        if (centerId == Guid.Empty || userId == Guid.Empty || !Enum.IsDefined(role))
        {
            throw new ArgumentException("Membership details are invalid.");
        }

        return new(Guid.NewGuid(), centerId, userId, role, createdAtUtc);
    }

    /// <summary>Roles an owner may hand out. Ownership itself only moves through a platform invitation.</summary>
    public static bool IsAssignableByOwner(CenterMemberRole role) =>
        role is CenterMemberRole.CenterAdmin or CenterMemberRole.Teacher or CenterMemberRole.Staff;

    public void ChangeRole(CenterMemberRole role)
    {
        if (Role == CenterMemberRole.CenterOwner || !IsAssignableByOwner(role))
        {
            throw new InvalidOperationException("The owner role cannot be changed here.");
        }

        Role = role;
    }

    /// <summary>A returning member keeps one row per center; re-joining reactivates it with the new role.</summary>
    public void Rejoin(CenterMemberRole role)
    {
        if (IsActive || Role == CenterMemberRole.CenterOwner)
        {
            throw new InvalidOperationException("Only an inactive non-owner membership can rejoin.");
        }

        Role = role;
        IsActive = true;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate()
    {
        if (Role == CenterMemberRole.CenterOwner)
        {
            throw new InvalidOperationException("Owner memberships are managed by the platform.");
        }

        IsActive = true;
    }
}
