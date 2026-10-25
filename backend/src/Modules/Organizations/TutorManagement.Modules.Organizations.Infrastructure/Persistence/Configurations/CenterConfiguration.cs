using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Configurations;

public sealed class CenterConfiguration : IEntityTypeConfiguration<Center>
{
    public void Configure(EntityTypeBuilder<Center> builder)
    {
        builder.ToTable("centers");
        builder.HasKey(center => center.Id);
        builder.Property(center => center.Id).HasColumnName("id");
        builder.Property(center => center.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        builder.Property(center => center.DisplayName).HasColumnName("display_name").HasMaxLength(150).IsRequired();
        builder.Property(center => center.TaxCode).HasColumnName("tax_code").HasMaxLength(14).IsRequired();
        builder.HasIndex(center => center.TaxCode).HasDatabaseName("ux_centers_tax_code").IsUnique();
        builder.Property(center => center.BusinessRegistrationNumber)
            .HasColumnName("business_registration_number").HasMaxLength(50);
        builder.Property(center => center.Address).HasColumnName("address").HasMaxLength(500).IsRequired();
        builder.Property(center => center.ContactEmail).HasColumnName("contact_email").HasMaxLength(254).IsRequired();
        builder.Property(center => center.ContactEmailVerifiedAtUtc).HasColumnName("contact_email_verified_at_utc");
        builder.Property(center => center.ContactPhone).HasColumnName("contact_phone").HasMaxLength(20).IsRequired();
        builder.Property(center => center.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(center => center.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(center => center.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired().IsConcurrencyToken();
        builder.Property(center => center.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(center => center.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(center => center.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        builder.HasIndex(center => center.Status).HasDatabaseName("ix_centers_status");
        builder.Property(center => center.ListedInDirectory).HasColumnName("listed_in_directory").HasDefaultValue(false);
        builder.HasMany(center => center.StatusHistory).WithOne().HasForeignKey(change => change.CenterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(center => center.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_statusHistory");
    }
}

public sealed class CenterStatusChangeConfiguration : IEntityTypeConfiguration<CenterStatusChange>
{
    public void Configure(EntityTypeBuilder<CenterStatusChange> builder)
    {
        builder.ToTable("center_status_history");
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(change => change.CenterId).HasColumnName("center_id");
        builder.Property(change => change.FromStatus).HasColumnName("from_status").HasConversion<string>().HasMaxLength(30);
        builder.Property(change => change.ToStatus).HasColumnName("to_status").HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(change => change.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(change => change.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(change => change.ChangedAtUtc).HasColumnName("changed_at_utc");
        builder.HasIndex(change => new { change.CenterId, change.ChangedAtUtc }).HasDatabaseName("ix_center_status_history_center");
    }
}
