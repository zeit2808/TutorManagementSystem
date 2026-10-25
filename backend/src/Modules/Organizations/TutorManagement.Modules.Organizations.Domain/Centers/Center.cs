using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Organizations.Domain.Centers;

public sealed class Center : AggregateRoot<Guid>
{
    private readonly List<CenterStatusChange> _statusHistory = [];

    private Center() : base(Guid.Empty) { }

    private Center(
        Guid id,
        string legalName,
        string displayName,
        string taxCode,
        string? businessRegistrationNumber,
        string address,
        string contactEmail,
        string contactPhone,
        DateTimeOffset createdAtUtc) : base(id)
    {
        LegalName = legalName;
        DisplayName = displayName;
        TaxCode = taxCode;
        BusinessRegistrationNumber = businessRegistrationNumber;
        Address = address;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        Status = CenterStatus.PendingApproval;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
        _statusHistory.Add(new CenterStatusChange(id, null, CenterStatus.PendingApproval, null, null, createdAtUtc));
    }

    public string LegalName { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string TaxCode { get; private set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string ContactEmail { get; private set; } = string.Empty;
    public DateTimeOffset? ContactEmailVerifiedAtUtc { get; private set; }
    public string ContactPhone { get; private set; } = string.Empty;
    public CenterStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewNote { get; private set; }
    /// <summary>D4: the owner opts in before students can find the center in the public directory.</summary>
    public bool ListedInDirectory { get; private set; }
    public IReadOnlyCollection<CenterStatusChange> StatusHistory => _statusHistory;

    public static Center Create(
        string legalName,
        string displayName,
        string taxCode,
        string? businessRegistrationNumber,
        string address,
        string contactEmail,
        string contactPhone,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            legalName.Trim(),
            displayName.Trim(),
            taxCode.Trim(),
            string.IsNullOrWhiteSpace(businessRegistrationNumber)
                ? null
                : businessRegistrationNumber.Trim(),
            address.Trim(),
            contactEmail.Trim(),
            contactPhone.Trim(),
            createdAtUtc);

    public void ApplyApprovedChange(CenterChangeRequest request, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CenterId != Id || request.Status != CenterChangeStatus.Approved ||
            !request.MatchesOriginal(this))
        {
            throw new InvalidOperationException("The change request does not match the current center.");
        }
        if (Status != CenterStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved center can apply a change request.");
        }

        LegalName = request.ProposedLegalName;
        DisplayName = request.ProposedDisplayName;
        TaxCode = request.ProposedTaxCode;
        BusinessRegistrationNumber = request.ProposedBusinessRegistrationNumber;
        Address = request.ProposedRegisteredAddress;
        if (!string.Equals(ContactEmail, request.ProposedContactEmail, StringComparison.OrdinalIgnoreCase))
            ContactEmailVerifiedAtUtc = null;
        ContactEmail = request.ProposedContactEmail;
        ContactPhone = request.ProposedContactPhone;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void Approve(Guid reviewerUserId, DateTimeOffset reviewedAtUtc)
    {
        if (Status == CenterStatus.Approved)
        {
            return;
        }

        // D8: a rejected center must be corrected and resubmitted before it can be approved.
        if (Status != CenterStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only a pending center can be approved.");
        }

        Record(CenterStatus.Approved, reviewerUserId, null, reviewedAtUtc);
        Status = CenterStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        ReviewNote = null;
        UpdatedAtUtc = reviewedAtUtc;
    }

    public void Reject(Guid reviewerUserId, string reason, DateTimeOffset reviewedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != CenterStatus.PendingApproval)
        {
            throw new InvalidOperationException("Only a pending center can be rejected.");
        }

        Record(CenterStatus.Rejected, reviewerUserId, reason, reviewedAtUtc);
        Status = CenterStatus.Rejected;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        ReviewNote = reason.Trim();
        UpdatedAtUtc = reviewedAtUtc;
    }

    public void Suspend(Guid reviewerUserId, string reason, DateTimeOffset reviewedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != CenterStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved center can be suspended.");
        }

        Record(CenterStatus.Suspended, reviewerUserId, reason, reviewedAtUtc);
        Status = CenterStatus.Suspended;
        ListedInDirectory = false;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        ReviewNote = reason.Trim();
        UpdatedAtUtc = reviewedAtUtc;
    }

    public void Reinstate(Guid reviewerUserId, string reason, DateTimeOffset reviewedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Status != CenterStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended center can be reinstated.");
        }

        Record(CenterStatus.Approved, reviewerUserId, reason, reviewedAtUtc);
        Status = CenterStatus.Approved;
        ReviewedByUserId = reviewerUserId;
        ReviewedAtUtc = reviewedAtUtc;
        ReviewNote = reason.Trim();
        UpdatedAtUtc = reviewedAtUtc;
    }

    /// <summary>
    /// D8: after a rejection the application is corrected and sent back for review. Review fields are
    /// cleared; the rejection stays in <see cref="StatusHistory"/>.
    /// </summary>
    public void Resubmit(CenterChangeProposal details, Guid actorUserId, string? note, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Status != CenterStatus.Rejected)
        {
            throw new InvalidOperationException("Only a rejected center can be resubmitted.");
        }

        LegalName = details.LegalName.Trim();
        DisplayName = details.DisplayName.Trim();
        TaxCode = details.TaxCode.Trim();
        BusinessRegistrationNumber = string.IsNullOrWhiteSpace(details.BusinessRegistrationNumber)
            ? null
            : details.BusinessRegistrationNumber.Trim();
        Address = details.RegisteredAddress.Trim();
        ContactEmail = details.ContactEmail.Trim();
        ContactPhone = details.ContactPhone.Trim();
        Record(CenterStatus.PendingApproval, actorUserId, note, nowUtc);
        Status = CenterStatus.PendingApproval;
        ReviewedAtUtc = null;
        ReviewedByUserId = null;
        ReviewNote = null;
        UpdatedAtUtc = nowUtc;
    }

    public void SetDirectoryListing(bool listed, DateTimeOffset nowUtc)
    {
        if (listed && Status != CenterStatus.Approved)
        {
            throw new InvalidOperationException("Only an approved center can be listed in the directory.");
        }

        if (ListedInDirectory == listed) return;
        ListedInDirectory = listed;
        UpdatedAtUtc = nowUtc;
    }

    private void Record(CenterStatus toStatus, Guid? actorUserId, string? note, DateTimeOffset nowUtc) =>
        _statusHistory.Add(new CenterStatusChange(Id, Status, toStatus, actorUserId,
            string.IsNullOrWhiteSpace(note) ? null : note.Trim(), nowUtc));
}
