using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(request => request.Username)
            .NotEmpty()
            .Length(3, 50)
            .Matches("^[a-zA-Z0-9._@-]+$")
            .WithMessage("Username contains unsupported characters.");

        RuleFor(request => request.Password)
            .NotEmpty()
            .Length(8, 128);
    }
}
