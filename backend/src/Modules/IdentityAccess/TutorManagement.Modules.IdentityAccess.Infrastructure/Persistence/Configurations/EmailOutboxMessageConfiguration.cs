using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

public sealed class EmailOutboxMessageConfiguration : IEntityTypeConfiguration<EmailOutboxMessage>
{
    public void Configure(EntityTypeBuilder<EmailOutboxMessage> builder)
    {
        builder.ToTable("email_outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).HasColumnName("id");
        builder.Property(message => message.Recipient)
            .HasColumnName("recipient")
            .HasMaxLength(254)
            .IsRequired();
        builder.Property(message => message.Subject)
            .HasColumnName("subject")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(message => message.Body)
            .HasColumnName("body")
            .HasMaxLength(4000)
            .IsRequired();
        builder.Property(message => message.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();
        builder.Property(message => message.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count")
            .IsRequired();
        builder.Property(message => message.LastError)
            .HasColumnName("last_error")
            .HasMaxLength(1000);
        builder.Property(message => message.SecurityTokenId).HasColumnName("security_token_id");
        builder.Property(message => message.ProcessingId)
            .HasColumnName("processing_id")
            .IsConcurrencyToken();
        builder.Property(message => message.ProcessingUntilUtc)
            .HasColumnName("processing_until_utc");
        builder.HasIndex(message => message.SecurityTokenId);
        builder.HasOne<UserSecurityToken>().WithMany()
            .HasForeignKey(message => message.SecurityTokenId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.CreatedAtUtc });
        builder.HasIndex(message => new { message.ProcessedAtUtc, message.ProcessingUntilUtc, message.CreatedAtUtc });
    }
}
