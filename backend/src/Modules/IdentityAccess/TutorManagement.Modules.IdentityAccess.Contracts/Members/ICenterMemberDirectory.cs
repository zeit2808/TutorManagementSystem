namespace TutorManagement.Modules.IdentityAccess.Contracts.Members;

public sealed record CenterMemberSummary(Guid UserId, string Role, string DisplayName);

/// <summary>Read-only view of center membership for other modules (e.g. linking a teacher profile).</summary>
public interface ICenterMemberDirectory
{
    Task<CenterMemberSummary?> FindActiveAsync(Guid centerId, Guid userId, CancellationToken cancellationToken);
}
