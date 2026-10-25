using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class UserSecurityTokenConfiguration : IEntityTypeConfiguration<UserSecurityToken>
{
    public void Configure(EntityTypeBuilder<UserSecurityToken> builder)
    {
        builder.ToTable("user_security_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id");
        builder.Property(token => token.UserId).HasColumnName("user_id");
        builder.Property(token => token.Purpose)
            .HasColumnName("purpose")
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(token => token.TokenHash)
            .HasDatabaseName("ux_user_security_tokens_token_hash")
            .IsUnique();
        builder.HasIndex(token => new { token.UserId, token.Purpose, token.ExpiresAtUtc });
        builder.HasIndex(token => new { token.UserId, token.Purpose })
            .HasDatabaseName("ux_user_security_tokens_unconsumed")
            .HasFilter("used_at_utc IS NULL AND revoked_at_utc IS NULL")
            .IsUnique();
        builder.Property(token => token.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();
        builder.Property(token => token.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property(token => token.UsedAtUtc).HasColumnName("used_at_utc");
        builder.Property(token => token.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(token => token.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(50);
        builder.Property(token => token.FailedAttemptCount).HasColumnName("failed_attempt_count").IsRequired();
        builder.Property(token => token.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
