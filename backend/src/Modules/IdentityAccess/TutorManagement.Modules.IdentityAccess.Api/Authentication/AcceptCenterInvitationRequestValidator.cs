using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class AcceptCenterInvitationRequestValidator : AbstractValidator<AcceptCenterInvitationRequest>
{
    public AcceptCenterInvitationRequestValidator()
    {
        RuleFor(request => request.Token).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Username)
            .NotEmpty().MinimumLength(4).MaximumLength(50)
            .Matches(@"^[a-zA-Z0-9._-]+$")
            .WithMessage("Username may contain letters, digits, dot, underscore, and hyphen only.");
        PasswordRules.Apply(RuleFor(request => request.Password));
        RuleFor(request => request.PhoneNumber)
            .NotEmpty().MaximumLength(20)
            .Matches(@"^(?:\+84|84|0)(?:3|5|7|8|9)\d{8}$")
            .WithMessage("Phone number must be a valid Vietnamese mobile number.");
    }
}
