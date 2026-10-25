using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Application.Centers;

public sealed class CenterChangeRequestDto
{
    public Guid Id { get; set; }
    public Guid CenterId { get; set; }
    /// <summary>Set on the platform-wide queue.</summary>
    public string? CenterName { get; set; }
    public CenterChangeProposalDto Original { get; set; } = new();
    public CenterChangeProposalDto Proposed { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public string EvidenceReference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid RequestedByUserId { get; set; }
    /// <summary>Filled by the API from the identity module; null when the account no longer exists.</summary>
    public string? RequestedByName { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }

    public static CenterChangeRequestDto From(CenterChangeRequest request) => new()
    {
        Id = request.Id,
        CenterId = request.CenterId,
        Original = new CenterChangeProposalDto
        {
            LegalName = request.OriginalLegalName,
            DisplayName = request.OriginalDisplayName,
            TaxCode = request.OriginalTaxCode,
            BusinessRegistrationNumber = request.OriginalBusinessRegistrationNumber,
            RegisteredAddress = request.OriginalRegisteredAddress,
            ContactEmail = request.OriginalContactEmail,
            ContactPhone = request.OriginalContactPhone
        },
        Proposed = new CenterChangeProposalDto
        {
            LegalName = request.ProposedLegalName,
            DisplayName = request.ProposedDisplayName,
            TaxCode = request.ProposedTaxCode,
            BusinessRegistrationNumber = request.ProposedBusinessRegistrationNumber,
            RegisteredAddress = request.ProposedRegisteredAddress,
            ContactEmail = request.ProposedContactEmail,
            ContactPhone = request.ProposedContactPhone
        },
        Reason = request.Reason,
        EvidenceReference = request.EvidenceReference,
        Status = request.Status.ToString(),
        RequestedByUserId = request.RequestedByUserId,
        CreatedAtUtc = request.CreatedAtUtc,
        ReviewedByUserId = request.ReviewedByUserId,
        ReviewedAtUtc = request.ReviewedAtUtc,
        ReviewNote = request.ReviewNote
    };
}

public sealed class CenterChangeProposalDto
{
    public string LegalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public string RegisteredAddress { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
}
