using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Configurations;

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.ToTable("subjects");
        builder.HasKey(subject => subject.Id);
        builder.HasAlternateKey(subject => new { subject.CenterId, subject.Id });
        builder.Property(subject => subject.Id).HasColumnName("id");
        builder.Property(subject => subject.CenterId).HasColumnName("center_id").IsRequired();
        builder.Property(subject => subject.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(subject => subject.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(20).IsRequired();
        builder.Property(subject => subject.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(subject => subject.NormalizedName).HasColumnName("normalized_name").HasMaxLength(100).IsRequired();
        builder.Property(subject => subject.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(subject => subject.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(subject => subject.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(subject => subject.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(subject => subject.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(subject => subject.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(subject => subject.ArchivedByUserId).HasColumnName("archived_by_user_id");
        builder.Property(subject => subject.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(subject => subject.IsActive);
        builder.HasIndex(subject => new { subject.CenterId, subject.NormalizedCode })
            .HasDatabaseName("ux_subjects_center_code").IsUnique();
        builder.HasIndex(subject => new { subject.CenterId, subject.NormalizedName })
            .HasDatabaseName("ux_subjects_center_name").IsUnique();
        builder.HasIndex(subject => new { subject.CenterId, subject.ArchivedAtUtc, subject.NormalizedName })
            .HasDatabaseName("ix_subjects_center_active_name");
    }
}
