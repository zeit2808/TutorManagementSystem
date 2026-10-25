using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class AuthSessionRepository(IdentityDbContext dbContext) : IAuthSessionRepository
{
    public async Task AddAsync(AuthSession session, CancellationToken cancellationToken) =>
        await dbContext.AuthSessions.AddAsync(session, cancellationToken);

    public Task<AuthSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.AuthSessions.SingleOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public async Task<IReadOnlyList<AuthSession>> ListActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.AuthSessions.Where(session => session.UserId == userId && session.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

    public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.AuthSessions.AsNoTracking().AnyAsync(session =>
            session.Id == sessionId && session.UserId == userId && session.RevokedAtUtc == null && session.User.IsActive,
            cancellationToken);

    public async Task RevokeActiveAsync(Guid sessionId, DateTimeOffset atUtc, string reason, CancellationToken cancellationToken) =>
        await dbContext.AuthSessions.Where(session => session.Id == sessionId && session.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.RevokedAtUtc, atUtc)
                .SetProperty(session => session.RevocationReason, reason)
                .SetProperty(session => session.Version, session => session.Version + 1), cancellationToken);
}
