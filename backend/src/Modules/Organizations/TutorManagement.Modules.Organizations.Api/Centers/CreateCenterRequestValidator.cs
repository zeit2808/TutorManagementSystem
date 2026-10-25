using FluentValidation;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.Modules.Organizations.Api.Centers;

public sealed class CreateCenterRequestValidator : AbstractValidator<CreateCenterRequest>
{
    public CreateCenterRequestValidator()
    {
        RuleFor(request => request.LegalName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.DisplayName).NotEmpty().MaximumLength(150);
        RuleFor(request => request.TaxCode)
            .NotEmpty()
            .Matches(@"^\d{10}(?:-\d{3})?$")
            .WithMessage("Tax code must contain 10 digits, optionally followed by a 3-digit branch suffix.");
        RuleFor(request => request.BusinessRegistrationNumber).MaximumLength(50);
        RuleFor(request => request.Address).NotEmpty().MaximumLength(500);
        RuleFor(request => request.ContactEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.ContactPhone)
            .NotEmpty()
            .Matches(@"^(?:\+84|0)(?:3|5|7|8|9)\d{8}$")
            .WithMessage("Contact phone must be a valid Vietnamese mobile number.");
    }
}
