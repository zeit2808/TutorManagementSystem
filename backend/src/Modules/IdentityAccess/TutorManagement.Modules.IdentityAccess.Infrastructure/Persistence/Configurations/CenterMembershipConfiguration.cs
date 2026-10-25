using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class CenterMembershipConfiguration : IEntityTypeConfiguration<CenterMembership>
{
    public void Configure(EntityTypeBuilder<CenterMembership> builder)
    {
        builder.ToTable("center_memberships");
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).HasColumnName("id");
        builder.Property(membership => membership.CenterId).HasColumnName("center_id").IsRequired();
        builder.Property(membership => membership.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(membership => membership.Role)
            .HasColumnName("role").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(membership => membership.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(membership => membership.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.HasIndex(membership => new { membership.CenterId, membership.UserId })
            .HasDatabaseName("ux_center_memberships_center_user").IsUnique();
        builder.HasIndex(membership => membership.UserId).HasDatabaseName("ix_center_memberships_user_id");
        // Decision D2: an account owns at most one center at a time.
        builder.HasIndex(membership => membership.UserId, "ux_center_memberships_active_owner")
            .IsUnique().HasFilter("role = 'CenterOwner' AND is_active");
        builder.HasOne<User>().WithMany().HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
