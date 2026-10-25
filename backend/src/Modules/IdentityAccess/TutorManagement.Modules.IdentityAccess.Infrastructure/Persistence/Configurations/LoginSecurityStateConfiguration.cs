using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class LoginSecurityStateConfiguration : IEntityTypeConfiguration<LoginSecurityState>
{
    public void Configure(EntityTypeBuilder<LoginSecurityState> builder)
    {
        builder.ToTable("login_security_states");
        builder.HasKey(state => state.UserId);
        builder.Property(state => state.UserId).HasColumnName("user_id");
        builder.Property(state => state.FailedAttemptCount).HasColumnName("failed_attempt_count");
        builder.Property(state => state.WindowStartedAtUtc).HasColumnName("window_started_at_utc");
        builder.Property(state => state.BlockedUntilUtc).HasColumnName("blocked_until_utc");
        builder.Property(state => state.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasOne(state => state.User).WithOne().HasForeignKey<LoginSecurityState>(state => state.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
