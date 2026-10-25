using FluentValidation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class InviteCenterOwnerRequestValidator : AbstractValidator<InviteCenterOwnerRequest>
{
    public InviteCenterOwnerRequestValidator() =>
        RuleFor(request => request.Email).NotEmpty().EmailAddress().MaximumLength(254);
}
