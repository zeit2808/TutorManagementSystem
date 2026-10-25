namespace TutorManagement.Modules.Organizations.Contracts.Centers;

public sealed record RequestCenterChangeRequest(
    string LegalName,
    string DisplayName,
    string TaxCode,
    string? BusinessRegistrationNumber,
    string RegisteredAddress,
    string ContactEmail,
    string ContactPhone,
    string Reason,
    string EvidenceReference);
