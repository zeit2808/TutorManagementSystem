using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class SecurityAuditEventConfiguration : IEntityTypeConfiguration<SecurityAuditEvent>
{
    public void Configure(EntityTypeBuilder<SecurityAuditEvent> builder)
    {
        builder.ToTable("security_audit_events");
        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).HasColumnName("id");
        builder.Property(audit => audit.UserId).HasColumnName("user_id");
        builder.Property(audit => audit.EventType).HasColumnName("event_type").HasMaxLength(60).IsRequired();
        builder.Property(audit => audit.OccurredAtUtc).HasColumnName("occurred_at_utc");
        builder.Property(audit => audit.TraceId).HasColumnName("trace_id").HasMaxLength(100);
        builder.Property(audit => audit.ClientIp).HasColumnName("client_ip").HasMaxLength(45);
        builder.Property(audit => audit.UserAgent).HasColumnName("user_agent").HasMaxLength(256);
        builder.HasIndex(audit => new { audit.UserId, audit.OccurredAtUtc });
        builder.HasIndex(audit => audit.OccurredAtUtc);
        builder.HasOne(audit => audit.User).WithMany().HasForeignKey(audit => audit.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
