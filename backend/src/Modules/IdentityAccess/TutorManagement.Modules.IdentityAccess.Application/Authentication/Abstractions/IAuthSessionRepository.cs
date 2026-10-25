using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IAuthSessionRepository
{
    Task AddAsync(AuthSession session, CancellationToken cancellationToken);
    Task<AuthSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuthSession>> ListActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken);
    Task RevokeActiveAsync(Guid sessionId, DateTimeOffset atUtc, string reason, CancellationToken cancellationToken);
}
