using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Configurations;

public sealed class CenterChangeRequestConfiguration : IEntityTypeConfiguration<CenterChangeRequest>
{
    public void Configure(EntityTypeBuilder<CenterChangeRequest> builder)
    {
        builder.ToTable("center_change_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id");
        builder.Property(request => request.CenterId).HasColumnName("center_id").IsRequired();
        builder.Property(request => request.RequestedByUserId).HasColumnName("requested_by_user_id").IsRequired();
        builder.Property(request => request.OriginalLegalName).HasColumnName("original_legal_name").HasMaxLength(200).IsRequired();
        builder.Property(request => request.OriginalDisplayName).HasColumnName("original_display_name").HasMaxLength(150).IsRequired();
        builder.Property(request => request.OriginalTaxCode).HasColumnName("original_tax_code").HasMaxLength(14).IsRequired();
        builder.Property(request => request.OriginalBusinessRegistrationNumber).HasColumnName("original_business_registration_number").HasMaxLength(50);
        builder.Property(request => request.OriginalRegisteredAddress).HasColumnName("original_registered_address").HasMaxLength(500).IsRequired();
        builder.Property(request => request.OriginalContactEmail).HasColumnName("original_contact_email").HasMaxLength(254).IsRequired();
        builder.Property(request => request.OriginalContactPhone).HasColumnName("original_contact_phone").HasMaxLength(20).IsRequired();
        builder.Property(request => request.ProposedLegalName).HasColumnName("proposed_legal_name").HasMaxLength(200).IsRequired();
        builder.Property(request => request.ProposedDisplayName).HasColumnName("proposed_display_name").HasMaxLength(150).IsRequired();
        builder.Property(request => request.ProposedTaxCode).HasColumnName("proposed_tax_code").HasMaxLength(14).IsRequired();
        builder.Property(request => request.ProposedBusinessRegistrationNumber).HasColumnName("proposed_business_registration_number").HasMaxLength(50);
        builder.Property(request => request.ProposedRegisteredAddress).HasColumnName("proposed_registered_address").HasMaxLength(500).IsRequired();
        builder.Property(request => request.ProposedContactEmail).HasColumnName("proposed_contact_email").HasMaxLength(254).IsRequired();
        builder.Property(request => request.ProposedContactPhone).HasColumnName("proposed_contact_phone").HasMaxLength(20).IsRequired();
        builder.Property(request => request.Reason).HasColumnName("reason").HasMaxLength(1000).IsRequired();
        builder.Property(request => request.EvidenceReference).HasColumnName("evidence_reference").HasMaxLength(500).IsRequired();
        builder.Property(request => request.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
        builder.Property(request => request.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(request => request.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(request => request.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(request => request.ReviewNote).HasColumnName("review_note").HasMaxLength(1000);
        builder.HasOne<Center>().WithMany().HasForeignKey(request => request.CenterId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => request.CenterId)
            .IsUnique()
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ux_center_change_requests_pending");
        builder.HasIndex(request => new { request.CenterId, request.CreatedAtUtc });
    }
}
