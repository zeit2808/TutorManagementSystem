using FluentValidation;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.Modules.Organizations.Api.Centers;

public sealed class RequestCenterChangeRequestValidator : AbstractValidator<RequestCenterChangeRequest>
{
    public RequestCenterChangeRequestValidator()
    {
        RuleFor(request => request.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(request => request.TaxCode).NotEmpty().Matches(@"^\d{10}(?:-\d{3})?$");
        RuleFor(request => request.BusinessRegistrationNumber).MaximumLength(50);
        RuleFor(request => request.RegisteredAddress).NotEmpty().MaximumLength(500);
        RuleFor(request => request.ContactEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.ContactPhone).NotEmpty()
            .Matches(@"^(?:\+84|0)(?:3|5|7|8|9)\d{8}$");
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(1000);
        RuleFor(request => request.EvidenceReference).NotEmpty().MaximumLength(500);
    }
}

public sealed class ReviewCenterChangeRequestValidator : AbstractValidator<ReviewCenterChangeRequest>
{
    public ReviewCenterChangeRequestValidator() =>
        RuleFor(request => request.ReviewNote).NotEmpty().MaximumLength(1000);
}
