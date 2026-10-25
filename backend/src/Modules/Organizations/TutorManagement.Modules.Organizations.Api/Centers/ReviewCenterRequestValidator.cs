using FluentValidation;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.Modules.Organizations.Api.Centers;

public sealed class ReviewCenterRequestValidator : AbstractValidator<ReviewCenterRequest>
{
    public ReviewCenterRequestValidator() =>
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(1000);
}
