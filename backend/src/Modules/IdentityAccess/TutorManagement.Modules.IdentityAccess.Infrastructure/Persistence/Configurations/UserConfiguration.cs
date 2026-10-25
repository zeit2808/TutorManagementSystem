using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).HasColumnName("id");
        builder.Property(user => user.Username)
            .HasColumnName("username")
            .HasMaxLength(50);
        builder.Property(user => user.NormalizedUsername)
            .HasColumnName("normalized_username")
            .HasMaxLength(50);
        builder.HasIndex(user => user.NormalizedUsername)
            .HasDatabaseName("ux_users_normalized_username")
            .IsUnique();
        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(user => user.NormalizedEmail)
            .HasColumnName("normalized_email")
            .HasMaxLength(254)
            .IsRequired();
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("ux_users_normalized_email")
            .IsUnique();
        builder.Property(user => user.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);
        builder.HasIndex(user => user.PhoneNumber)
            .HasDatabaseName("ux_users_phone_number")
            .IsUnique();
        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(512);
        builder.Ignore(user => user.HasLocalPassword);
        builder.Property(user => user.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property(user => user.EmailVerifiedAtUtc)
            .HasColumnName("email_verified_at_utc");
        builder.Ignore(user => user.IsEmailVerified);
        builder.Navigation(user => user.UserRoles)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
