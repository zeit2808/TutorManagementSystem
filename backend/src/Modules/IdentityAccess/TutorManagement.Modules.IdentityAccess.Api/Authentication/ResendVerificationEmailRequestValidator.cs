using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class ResendVerificationEmailRequestValidator
    : AbstractValidator<ResendVerificationEmailRequest>
{
    public ResendVerificationEmailRequestValidator()
    {
        RuleFor(request => request.Email)
            .NotEmpty()
            .MaximumLength(254)
            .EmailAddress();
    }
}
