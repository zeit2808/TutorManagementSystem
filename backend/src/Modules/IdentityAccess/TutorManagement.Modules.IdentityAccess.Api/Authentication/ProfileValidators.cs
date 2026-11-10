using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class UpdateMyProfileRequestValidator : AbstractValidator<UpdateMyProfileRequest>
{
    public UpdateMyProfileRequestValidator()
    {
        RuleFor(request => request.DisplayName).MaximumLength(100);
        RuleFor(request => request.PhoneNumber).MaximumLength(20);
    }
}

public sealed class RequestEmailChangeRequestValidator : AbstractValidator<RequestEmailChangeRequest>
{
    public RequestEmailChangeRequestValidator()
    {
        RuleFor(request => request.NewEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.CurrentPassword).NotEmpty().MaximumLength(256);
    }
}

public sealed class ConfirmEmailChangeRequestValidator : AbstractValidator<ConfirmEmailChangeRequest>
{
    public ConfirmEmailChangeRequestValidator()
    {
        RuleFor(request => request.NewEmail).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Otp).NotEmpty().Matches("^[0-9]{8}$")
            .WithMessage("OTP must contain eight digits.");
    }
}
