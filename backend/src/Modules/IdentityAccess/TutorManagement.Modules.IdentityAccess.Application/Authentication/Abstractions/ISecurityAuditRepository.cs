using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface ISecurityAuditRepository
{
    Task AddAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken);

    // Used only after a failed EF concurrency save leaves the current unit of work unusable.
    Task RecordIsolatedAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken);
}
