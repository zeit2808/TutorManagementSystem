using FluentValidation;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Api.Teaching;

public sealed class CreateClassRequestValidator : AbstractValidator<CreateClassRequest>
{
    public CreateClassRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.Code).Must(TeachingClass.IsValidCode);
        RuleFor(x => x.Name).Must(TeachingClass.IsValidName);
        RuleFor(x => x.MaxStudents).InclusiveBetween(1, 500);
    }
}

public sealed class UpdateClassRequestValidator : AbstractValidator<UpdateClassRequest>
{
    public UpdateClassRequestValidator()
    {
        RuleFor(x => x.Name).Must(TeachingClass.IsValidName);
        RuleFor(x => x.MaxStudents).InclusiveBetween(1, 500);
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateStudentRequestValidator : AbstractValidator<CreateStudentRequest>
{
    public CreateStudentRequestValidator()
    {
        RuleFor(x => x.Code).Must(code => string.IsNullOrWhiteSpace(code) || TeachingClass.IsValidCode(code))
            .WithMessage("Code must be 2–20 letters, digits, '-' or '_', or empty to generate one.");
        RuleFor(x => x.FullName).Must(TeachingClass.IsValidName);
        RuleFor(x => x.ParentName).Must(ParentContact.IsValidName).WithMessage("Parent name must contain 2–100 characters.");
        RuleFor(x => x.ParentPhone).Must(phone => ParentContact.TryNormalizePhone(phone, out _))
            .WithMessage("Parent phone must be a Vietnamese mobile number.");
    }
}

public sealed class UpdateStudentRequestValidator : AbstractValidator<UpdateStudentRequest>
{
    public UpdateStudentRequestValidator()
    {
        RuleFor(x => x.FullName).Must(TeachingClass.IsValidName);
        RuleFor(x => x.ParentName).Must(ParentContact.IsValidName).WithMessage("Parent name must contain 2–100 characters.");
        RuleFor(x => x.ParentPhone).Must(phone => ParentContact.TryNormalizePhone(phone, out _))
            .WithMessage("Parent phone must be a Vietnamese mobile number.");
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateTeacherRequestValidator : AbstractValidator<CreateTeacherRequest>
{
    public CreateTeacherRequestValidator()
    {
        RuleFor(x => x.Code).Must(TeachingClass.IsValidCode);
        RuleFor(x => x.FullName).Must(TeachingClass.IsValidName);
    }
}

public sealed class UpdateTeacherRequestValidator : AbstractValidator<UpdateTeacherRequest>
{
    public UpdateTeacherRequestValidator()
    {
        RuleFor(x => x.FullName).Must(TeachingClass.IsValidName);
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
    }
}

public sealed class EnrollStudentRequestValidator : AbstractValidator<EnrollStudentRequest>
{
    public EnrollStudentRequestValidator() => RuleFor(x => x.StudentId).NotEmpty();
}

public sealed class CreateSessionRequestValidator : AbstractValidator<CreateSessionRequest>
{
    public CreateSessionRequestValidator()
    {
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty();
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.StartUtc).Must(x => x.Offset == TimeSpan.Zero)
            .WithMessage("StartUtc must use UTC (+00:00).");
        RuleFor(x => x.EndUtc).Must(x => x.Offset == TimeSpan.Zero)
            .WithMessage("EndUtc must use UTC (+00:00).");
        RuleFor(x => x).Must(x => x.EndUtc > x.StartUtc &&
            x.EndUtc - x.StartUtc >= TimeSpan.FromMinutes(15) &&
            x.EndUtc - x.StartUtc <= TimeSpan.FromHours(8))
            .WithMessage("Session duration must be between 15 minutes and 8 hours.");
    }
}

public sealed class TeachingVersionRequestValidator : AbstractValidator<TeachingVersionRequest>
{
    public TeachingVersionRequestValidator() => RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
}

public sealed class RescheduleSessionRequestValidator : AbstractValidator<RescheduleSessionRequest>
{
    public RescheduleSessionRequestValidator()
    {
        RuleFor(x => x.TeacherId).NotEmpty();
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).Must(TeachingSession.IsValidReason)
            .WithMessage("A reason of 1–500 characters is required.");
        RuleFor(x => x.StartUtc).Must(x => x.Offset == TimeSpan.Zero)
            .WithMessage("StartUtc must use UTC (+00:00).");
        RuleFor(x => x.EndUtc).Must(x => x.Offset == TimeSpan.Zero)
            .WithMessage("EndUtc must use UTC (+00:00).");
        RuleFor(x => x).Must(x => x.EndUtc > x.StartUtc &&
            x.EndUtc - x.StartUtc >= TimeSpan.FromMinutes(15) &&
            x.EndUtc - x.StartUtc <= TimeSpan.FromHours(8))
            .WithMessage("Session duration must be between 15 minutes and 8 hours.");
    }
}

public sealed class SessionStatusChangeRequestValidator : AbstractValidator<SessionStatusChangeRequest>
{
    public SessionStatusChangeRequestValidator()
    {
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).Must(TeachingSession.IsValidReason)
            .WithMessage("A reason of 1–500 characters is required.");
    }
}

public sealed class CreateScheduleSeriesRequestValidator : AbstractValidator<CreateScheduleSeriesRequest>
{
    public CreateScheduleSeriesRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ClassId).NotEmpty();
        RuleFor(x => x.TeacherId).NotEmpty();
        RuleFor(x => x.RoomId).NotEmpty();
        RuleFor(x => x.TimeZoneId).Must(x => string.IsNullOrWhiteSpace(x) ||
                string.Equals(x, "Asia/Ho_Chi_Minh", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Schedules use Vietnam time; omit TimeZoneId or send Asia/Ho_Chi_Minh.");
        RuleFor(x => x.StartDate).NotEqual(default(DateOnly));
        RuleFor(x => x.EndDate).NotEqual(default(DateOnly));
        RuleFor(x => x).Must(x => x.EndDate >= x.StartDate &&
            x.EndDate.DayNumber - x.StartDate.DayNumber <= 179)
            .WithMessage("The inclusive schedule period may contain at most 180 days.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 480);
        RuleFor(x => x.Weekdays).NotNull().Must(x => x is { Count: >= 1 and <= 7 } &&
            x.All(day => Enum.TryParse<DayOfWeek>(day, true, out _) &&
                Enum.GetNames<DayOfWeek>().Any(name => name.Equals(day, StringComparison.OrdinalIgnoreCase))) &&
            x.Distinct(StringComparer.OrdinalIgnoreCase).Count() == x.Count)
            .WithMessage("Weekdays must contain 1–7 distinct English names, e.g. Monday and Thursday.");
        RuleFor(x => x.ExcludedDates).Must((request, dates) =>
            dates is null || (dates.Count <= 180 && dates.Distinct().Count() == dates.Count &&
                dates.All(date => date >= request.StartDate && date <= request.EndDate &&
                    request.Weekdays is not null && request.Weekdays.Any(day =>
                        day.Equals(date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))))
            .WithMessage("ExcludedDates must be distinct scheduled weekdays inside the period.");
    }
}

public sealed class StopScheduleSeriesRequestValidator : AbstractValidator<StopScheduleSeriesRequest>
{
    public StopScheduleSeriesRequestValidator()
    {
        RuleFor(x => x.FromDate).NotEqual(default(DateOnly));
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
        // B6: the reason is shown to every affected student.
        RuleFor(x => x.Reason).NotEmpty().WithMessage("reason is required (1–500 characters).").MaximumLength(500);
    }
}

public sealed class ReplaceScheduleSeriesRequestValidator : AbstractValidator<ReplaceScheduleSeriesRequest>
{
    public ReplaceScheduleSeriesRequestValidator()
    {
        RuleFor(x => x.FromDate).NotEqual(default(DateOnly));
        RuleFor(x => x.ExpectedVersion).NotNull().GreaterThanOrEqualTo(0);
        RuleFor(x => x.Replacement).NotNull().SetValidator(new CreateScheduleSeriesRequestValidator());
        RuleFor(x => x.Reason).NotEmpty().WithMessage("reason is required (1–500 characters).").MaximumLength(500);
        RuleFor(x => x).Must(x => x.Replacement is null || x.Replacement.StartDate == x.FromDate)
            .WithMessage("Replacement.StartDate must equal FromDate.");
    }
}
