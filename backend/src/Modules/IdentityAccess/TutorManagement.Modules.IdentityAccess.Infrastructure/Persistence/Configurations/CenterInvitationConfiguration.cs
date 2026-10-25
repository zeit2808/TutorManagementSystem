using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class CenterInvitationConfiguration : IEntityTypeConfiguration<CenterInvitation>
{
    public void Configure(EntityTypeBuilder<CenterInvitation> builder)
    {
        builder.ToTable("center_invitations");
        builder.HasKey(invitation => invitation.Id);
        builder.Property(invitation => invitation.Id).HasColumnName("id");
        builder.Property(invitation => invitation.CenterId).HasColumnName("center_id").IsRequired();
        builder.Property(invitation => invitation.Email).HasColumnName("email").HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.NormalizedEmail)
            .HasColumnName("normalized_email").HasMaxLength(254).IsRequired();
        builder.Property(invitation => invitation.Role)
            .HasColumnName("role").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(invitation => invitation.TokenHash)
            .HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.HasIndex(invitation => invitation.TokenHash)
            .HasDatabaseName("ux_center_invitations_token_hash").IsUnique();
        builder.Property(invitation => invitation.ExpiresAtUtc).HasColumnName("expires_at_utc").IsRequired();
        builder.Property(invitation => invitation.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(invitation => invitation.InvitedByUserId).HasColumnName("invited_by_user_id").IsRequired();
        builder.Property(invitation => invitation.AcceptedAtUtc).HasColumnName("accepted_at_utc");
        builder.Property(invitation => invitation.AcceptedByUserId).HasColumnName("accepted_by_user_id");
        builder.Property(invitation => invitation.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(invitation => invitation.RevocationReason)
            .HasColumnName("revocation_reason").HasMaxLength(100);
        builder.HasIndex(invitation => new { invitation.CenterId, invitation.NormalizedEmail })
            .HasDatabaseName("ix_center_invitations_center_email");
        builder.HasOne<User>().WithMany().HasForeignKey(invitation => invitation.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(invitation => invitation.AcceptedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
