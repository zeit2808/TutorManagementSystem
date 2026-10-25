using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Organizations.Domain.Centers;

public sealed class CenterChangeRequest : Entity<Guid>
{
    private CenterChangeRequest() : base(Guid.Empty) { }

    private CenterChangeRequest(
        Center center, Guid requesterId, CenterChangeProposal proposal,
        string reason, string evidenceReference, DateTimeOffset nowUtc)
        : base(Guid.NewGuid())
    {
        CenterId = center.Id;
        RequestedByUserId = requesterId;
        OriginalLegalName = center.LegalName;
        OriginalDisplayName = center.DisplayName;
        OriginalTaxCode = center.TaxCode;
        OriginalBusinessRegistrationNumber = center.BusinessRegistrationNumber;
        OriginalRegisteredAddress = center.Address;
        OriginalContactEmail = center.ContactEmail;
        OriginalContactPhone = center.ContactPhone;
        ProposedLegalName = proposal.LegalName.Trim();
        ProposedDisplayName = proposal.DisplayName.Trim();
        ProposedTaxCode = proposal.TaxCode.Trim();
        ProposedBusinessRegistrationNumber = string.IsNullOrWhiteSpace(proposal.BusinessRegistrationNumber)
            ? null : proposal.BusinessRegistrationNumber.Trim();
        ProposedRegisteredAddress = proposal.RegisteredAddress.Trim();
        ProposedContactEmail = proposal.ContactEmail.Trim();
        ProposedContactPhone = proposal.ContactPhone.Trim();
        Reason = reason.Trim();
        EvidenceReference = evidenceReference.Trim();
        Status = CenterChangeStatus.Pending;
        CreatedAtUtc = nowUtc;
    }

    public Guid CenterId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string OriginalLegalName { get; private set; } = string.Empty;
    public string OriginalDisplayName { get; private set; } = string.Empty;
    public string OriginalTaxCode { get; private set; } = string.Empty;
    public string? OriginalBusinessRegistrationNumber { get; private set; }
    public string OriginalRegisteredAddress { get; private set; } = string.Empty;
    public string OriginalContactEmail { get; private set; } = string.Empty;
    public string OriginalContactPhone { get; private set; } = string.Empty;
    public string ProposedLegalName { get; private set; } = string.Empty;
    public string ProposedDisplayName { get; private set; } = string.Empty;
    public string ProposedTaxCode { get; private set; } = string.Empty;
    public string? ProposedBusinessRegistrationNumber { get; private set; }
    public string ProposedRegisteredAddress { get; private set; } = string.Empty;
    public string ProposedContactEmail { get; private set; } = string.Empty;
    public string ProposedContactPhone { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string EvidenceReference { get; private set; } = string.Empty;
    public CenterChangeStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public string? ReviewNote { get; private set; }

    public static CenterChangeRequest Create(
        Center center, Guid requesterId, CenterChangeProposal proposal,
        string reason, string evidenceReference, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);
        if (center.Status != CenterStatus.Approved)
            throw new InvalidOperationException("Only approved centers may request changes.");

        return new CenterChangeRequest(center, requesterId, proposal, reason, evidenceReference, nowUtc);
    }

    public bool MatchesOriginal(Center center) =>
        center.LegalName == OriginalLegalName && center.DisplayName == OriginalDisplayName &&
        center.TaxCode == OriginalTaxCode &&
        center.BusinessRegistrationNumber == OriginalBusinessRegistrationNumber &&
        center.Address == OriginalRegisteredAddress &&
        center.ContactEmail == OriginalContactEmail && center.ContactPhone == OriginalContactPhone;

    public bool HasChanges =>
        ProposedLegalName != OriginalLegalName || ProposedDisplayName != OriginalDisplayName ||
        ProposedTaxCode != OriginalTaxCode ||
        ProposedBusinessRegistrationNumber != OriginalBusinessRegistrationNumber ||
        ProposedRegisteredAddress != OriginalRegisteredAddress ||
        ProposedContactEmail != OriginalContactEmail || ProposedContactPhone != OriginalContactPhone;

    public void Approve(Guid reviewerId, string note, DateTimeOffset nowUtc) => Review(
        CenterChangeStatus.Approved, reviewerId, note, nowUtc);

    public void Reject(Guid reviewerId, string note, DateTimeOffset nowUtc) => Review(
        CenterChangeStatus.Rejected, reviewerId, note, nowUtc);

    /// <summary>D10: the owner can take back a request the platform has not reviewed yet.</summary>
    public void Withdraw(Guid ownerId, DateTimeOffset nowUtc)
    {
        if (Status != CenterChangeStatus.Pending || ownerId == Guid.Empty)
        {
            throw new InvalidOperationException("Only a pending change request can be withdrawn.");
        }

        Status = CenterChangeStatus.Withdrawn;
        ReviewedByUserId = ownerId;
        ReviewedAtUtc = nowUtc;
        ReviewNote = "Withdrawn by the center owner.";
    }

    private void Review(CenterChangeStatus status, Guid reviewerId, string note, DateTimeOffset nowUtc)
    {
        if (Status != CenterChangeStatus.Pending)
            throw new InvalidOperationException("The change request was already reviewed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        Status = status;
        ReviewedByUserId = reviewerId;
        ReviewedAtUtc = nowUtc;
        ReviewNote = note.Trim();
    }
}

public sealed record CenterChangeProposal(
    string LegalName, string DisplayName, string TaxCode, string? BusinessRegistrationNumber,
    string RegisteredAddress, string ContactEmail, string ContactPhone);
