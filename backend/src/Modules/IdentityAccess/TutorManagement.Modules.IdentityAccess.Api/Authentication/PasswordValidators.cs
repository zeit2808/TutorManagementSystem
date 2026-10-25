using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

internal static class PasswordRules
{
    public static IRuleBuilderOptions<T, string> Apply<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(PasswordPolicy.IsValid)
            .WithMessage("Password must be 12–128 characters, contain uppercase, lowercase, digit and symbol, and avoid common patterns.");
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(request => request.CurrentPassword).NotEmpty();
        PasswordRules.Apply(RuleFor(request => request.NewPassword));
        RuleFor(request => request.ConfirmPassword).Equal(request => request.NewPassword)
            .WithMessage("Password confirmation does not match.");
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
        RuleFor(request => request.Otp).NotEmpty().Matches("^[0-9]{8}$")
            .WithMessage("OTP must contain eight digits.");
        PasswordRules.Apply(RuleFor(request => request.NewPassword));
        RuleFor(request => request.ConfirmPassword).Equal(request => request.NewPassword)
            .WithMessage("Password confirmation does not match.");
    }
}
