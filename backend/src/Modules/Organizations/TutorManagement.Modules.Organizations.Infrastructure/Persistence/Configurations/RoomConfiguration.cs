using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.ToTable("rooms");
        builder.HasKey(room => room.Id);
        builder.HasAlternateKey(room => new { room.CenterId, room.Id });
        builder.Property(room => room.Id).HasColumnName("id");
        builder.Property(room => room.CenterId).HasColumnName("center_id").IsRequired();
        builder.HasOne<Center>().WithMany().HasForeignKey(room => room.CenterId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(room => room.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(room => room.NormalizedCode).HasColumnName("normalized_code").HasMaxLength(20).IsRequired();
        builder.Property(room => room.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(room => room.NormalizedName).HasColumnName("normalized_name").HasMaxLength(100).IsRequired();
        builder.Property(room => room.LocationNote).HasColumnName("location_note").HasMaxLength(200);
        builder.Property(room => room.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(room => room.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(room => room.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(room => room.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
        builder.Property(room => room.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(room => room.ArchivedByUserId).HasColumnName("archived_by_user_id");
        builder.Property(room => room.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(room => room.IsActive);
        builder.HasIndex(room => new { room.CenterId, room.NormalizedCode })
            .HasDatabaseName("ux_rooms_center_code").IsUnique();
        builder.HasIndex(room => new { room.CenterId, room.NormalizedName })
            .HasDatabaseName("ux_rooms_center_name").IsUnique();
        builder.HasIndex(room => new { room.CenterId, room.ArchivedAtUtc, room.NormalizedName })
            .HasDatabaseName("ix_rooms_center_active_name");
    }
}
