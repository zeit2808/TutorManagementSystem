namespace TutorManagement.Modules.IdentityAccess.Contracts.Members;

public sealed record CenterMemberSummary(Guid UserId, string Role, string DisplayName);

/// <summary>Read-only view of center membership for other modules (e.g. linking a teacher profile).</summary>
public interface ICenterMemberDirectory
{
    Task<CenterMemberSummary?> FindActiveAsync(Guid centerId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Active members of a center holding one of the roles (e.g. "CenterOwner"), for alerts sent by the system.</summary>
    Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid centerId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken);
}
