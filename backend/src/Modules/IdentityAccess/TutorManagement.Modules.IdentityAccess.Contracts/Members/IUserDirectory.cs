namespace TutorManagement.Modules.IdentityAccess.Contracts.Members;

/// <summary>Display names for user IDs recorded by other modules (who reviewed, who corrected, …).</summary>
public interface IUserDirectory
{
    /// <summary>Username, or email when the account has no username. Unknown IDs are left out.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds,
        CancellationToken cancellationToken);
}
