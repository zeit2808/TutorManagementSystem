using TutorManagement.Modules.Organizations.Domain.Centers;

namespace TutorManagement.Modules.Organizations.Application.Centers;

public sealed class CenterDto
{
    public CenterDto() { }

    private CenterDto(Center center)
    {
        Id = center.Id;
        LegalName = center.LegalName;
        DisplayName = center.DisplayName;
        TaxCode = center.TaxCode;
        BusinessRegistrationNumber = center.BusinessRegistrationNumber;
        Address = center.Address;
        ContactEmail = center.ContactEmail;
        ContactPhone = center.ContactPhone;
        Status = center.Status.ToString();
        CreatedAtUtc = center.CreatedAtUtc;
        ReviewedAtUtc = center.ReviewedAtUtc;
        ReviewNote = center.ReviewNote;
        ListedInDirectory = center.ListedInDirectory;
    }

    public Guid Id { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string TaxCode { get; set; } = string.Empty;
    public string? BusinessRegistrationNumber { get; set; }
    public string Address { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public bool ListedInDirectory { get; set; }

    public static CenterDto From(Center center) => new(center);
}
