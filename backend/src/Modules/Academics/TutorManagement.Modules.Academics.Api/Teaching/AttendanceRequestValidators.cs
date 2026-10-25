using FluentValidation;
using TutorManagement.Modules.Academics.Contracts.Teaching;

namespace TutorManagement.Modules.Academics.Api.Teaching;

public sealed class SaveAttendanceDraftRequestValidator : AbstractValidator<SaveAttendanceDraftRequest>
{
    public SaveAttendanceDraftRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActualTeacherId).Must(x => !x.HasValue || x.Value != Guid.Empty);
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.DefaultMark).NotEmpty();
        RuleFor(x => x.Overrides).NotNull().Must(x => x is { Count: <= 500 });
        RuleForEach(x => x.Overrides).ChildRules(item =>
        {
            item.RuleFor(x => x.StudentId).NotEmpty();
            item.RuleFor(x => x.Mark).NotEmpty();
            item.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}

public sealed class CorrectAttendanceRequestValidator : AbstractValidator<CorrectAttendanceRequest>
{
    public CorrectAttendanceRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.ActualTeacherId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500);
        RuleFor(x => x.Reason).NotEmpty().MinimumLength(10).MaximumLength(500);
        RuleFor(x => x.Changes).NotNull().Must(x => x is { Count: <= 500 });
        RuleForEach(x => x.Changes).ChildRules(item =>
        {
            item.RuleFor(x => x.StudentId).NotEmpty();
            item.RuleFor(x => x.Mark).NotEmpty();
            item.RuleFor(x => x.Note).MaximumLength(500);
        });
    }
}
