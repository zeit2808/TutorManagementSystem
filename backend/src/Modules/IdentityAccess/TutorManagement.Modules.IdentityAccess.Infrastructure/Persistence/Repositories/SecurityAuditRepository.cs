using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class SecurityAuditRepository(
    IdentityDbContext dbContext,
    DbContextOptions<IdentityDbContext> options) : ISecurityAuditRepository
{
    public async Task AddAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken) =>
        await dbContext.SecurityAuditEvents.AddAsync(auditEvent, cancellationToken);

    public async Task RecordIsolatedAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await using var isolatedContext = new IdentityDbContext(options);
        await isolatedContext.SecurityAuditEvents.AddAsync(auditEvent, cancellationToken);
        await isolatedContext.SaveChangesAsync(cancellationToken);
    }
}
