using Microsoft.EntityFrameworkCore;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

public sealed class SecurityAuditRetentionProcessor(IdentityDbContext dbContext, int retentionDays)
{
    public Task<int> DeleteExpiredBatchAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        // Bound each transaction so retention never monopolizes the email worker.
        return dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM iam.security_audit_events
            WHERE id IN (
                SELECT id FROM iam.security_audit_events
                WHERE occurred_at_utc < {cutoff}
                ORDER BY occurred_at_utc
                LIMIT 1000
            )
            """, cancellationToken);
    }
}
