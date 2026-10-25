namespace TutorManagement.Modules.Organizations.Contracts.Centers;

public sealed record CreateCenterRequest(
    string LegalName,
    string DisplayName,
    string TaxCode,
    string? BusinessRegistrationNumber,
    string Address,
    string ContactEmail,
    string ContactPhone);
