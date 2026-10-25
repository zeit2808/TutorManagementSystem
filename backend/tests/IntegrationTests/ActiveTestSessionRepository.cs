using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.IntegrationTests;

internal sealed class ActiveTestSessionRepository : IAuthSessionRepository
{
    public static readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");

    public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(sessionId == SessionId && userId != Guid.Empty);

    public Task AddAsync(AuthSession session, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AuthSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<AuthSession>> ListActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
    public Task RevokeActiveAsync(Guid sessionId, DateTimeOffset atUtc, string reason, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
