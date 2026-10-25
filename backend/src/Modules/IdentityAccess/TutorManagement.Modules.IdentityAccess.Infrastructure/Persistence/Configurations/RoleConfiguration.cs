using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(role => role.Id);
        builder.Property(role => role.Id).HasColumnName("id");
        builder.Property(role => role.Name)
            .HasColumnName("name")
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(role => role.NormalizedName)
            .HasColumnName("normalized_name")
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(role => role.NormalizedName).IsUnique();

        builder.HasData(
            new
            {
                Id = SystemRoles.PlatformAdminId,
                Name = SystemRoles.PlatformAdmin,
                NormalizedName = "PLATFORMADMIN"
            },
            new
            {
                Id = SystemRoles.ManagerId,
                Name = SystemRoles.Manager,
                NormalizedName = "MANAGER"
            },
            new
            {
                Id = SystemRoles.TeacherId,
                Name = SystemRoles.Teacher,
                NormalizedName = "TEACHER"
            },
            new
            {
                Id = SystemRoles.StudentId,
                Name = SystemRoles.Student,
                NormalizedName = "STUDENT"
            });
    }
}
