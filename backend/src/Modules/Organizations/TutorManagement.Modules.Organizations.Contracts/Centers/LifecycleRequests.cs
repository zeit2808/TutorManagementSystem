namespace TutorManagement.Modules.Organizations.Contracts.Centers;

/// <summary>Corrected application details sent back for review after a rejection (D8).</summary>
public sealed record ResubmitCenterRequest(
    string LegalName,
    string DisplayName,
    string TaxCode,
    string? BusinessRegistrationNumber,
    string Address,
    string ContactEmail,
    string ContactPhone,
    string? Note);

public sealed record SetDirectoryListingRequest(bool Listed);
