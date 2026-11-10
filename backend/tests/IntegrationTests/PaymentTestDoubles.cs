using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;

namespace TutorManagement.IntegrationTests;

/// <summary>Answers the password check with <see cref="Result"/> and remembers whether it was asked.</summary>
internal sealed class StubReauthenticator(ReauthenticationResult result = ReauthenticationResult.Verified) : IPasswordReauthenticator
{
    public ReauthenticationResult Result { get; set; } = result;
    public int Calls { get; private set; }

    public Task<ReauthenticationResult> VerifyAsync(Guid userId, string password, string? clientIp, string? userAgent,
        string? traceId, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

/// <summary>Every center has the same owners.</summary>
internal sealed class FixedOwners(params Guid[] owners) : ICenterMemberDirectory
{
    public Task<CenterMemberSummary?> FindActiveAsync(Guid centerId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<CenterMemberSummary?>(owners.Contains(userId) ? new CenterMemberSummary(userId, "CenterOwner", "owner") : null);

    public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid centerId, IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(roles.Contains("CenterOwner") ? owners : []);
}
