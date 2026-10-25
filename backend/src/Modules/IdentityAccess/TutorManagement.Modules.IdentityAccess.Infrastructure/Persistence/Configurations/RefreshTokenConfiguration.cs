using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).HasColumnName("id");
        builder.Property(token => token.UserId).HasColumnName("user_id");
        builder.Property(token => token.FamilyId)
            .HasColumnName("family_id")
            .IsRequired();
        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.ExpiresAtUtc });
        builder.HasIndex(token => new { token.FamilyId, token.ExpiresAtUtc });
        builder.Property(token => token.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();
        builder.Property(token => token.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property(token => token.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(token => token.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(token => token.RevocationReason)
            .HasColumnName("revocation_reason")
            .HasMaxLength(50);
        builder.Property(token => token.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();
        builder.HasOne(token => token.User)
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
