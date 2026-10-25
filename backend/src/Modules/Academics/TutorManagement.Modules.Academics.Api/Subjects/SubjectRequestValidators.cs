using FluentValidation;
using TutorManagement.Modules.Academics.Contracts.Subjects;
using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.Modules.Academics.Api.Subjects;

public sealed class CreateSubjectRequestValidator : AbstractValidator<CreateSubjectRequest>
{
    public CreateSubjectRequestValidator()
    {
        RuleFor(request => request.Code).Must(Subject.IsValidCode)
            .WithMessage("Code must have 2–20 ASCII letters, digits, hyphens or underscores.");
        RuleFor(request => request.Name).Must(Subject.IsValidName)
            .WithMessage("Name must have 2–100 characters and no control characters.");
        RuleFor(request => request.Description).Must(Subject.IsValidDescription)
            .WithMessage("Description must have at most 500 characters.");
    }
}

public sealed class SubjectVersionRequestValidator : AbstractValidator<SubjectVersionRequest>
{
    public SubjectVersionRequestValidator() => RuleFor(request => request.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
}

public sealed class UpdateSubjectRequestValidator : AbstractValidator<UpdateSubjectRequest>
{
    public UpdateSubjectRequestValidator()
    {
        RuleFor(request => request.Code).Must(Subject.IsValidCode)
            .WithMessage("Code must have 2–20 ASCII letters, digits, hyphens or underscores.");
        RuleFor(request => request.Name).Must(Subject.IsValidName)
            .WithMessage("Name must have 2–100 characters and no control characters.");
        RuleFor(request => request.Description).Must(Subject.IsValidDescription)
            .WithMessage("Description must have at most 500 characters.");
        RuleFor(request => request.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}
