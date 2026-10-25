using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.Academics.Api.Subjects;
using TutorManagement.Modules.Academics.Contracts.Subjects;
using TutorManagement.Modules.Academics.Api.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;

namespace TutorManagement.Modules.Academics.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddAcademicsApi(this IMvcBuilder builder)
    {
        builder.Services.AddScoped<IValidator<CreateSubjectRequest>, CreateSubjectRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdateSubjectRequest>, UpdateSubjectRequestValidator>();
        builder.Services.AddScoped<IValidator<SubjectVersionRequest>, SubjectVersionRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateClassRequest>, CreateClassRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdateClassRequest>, UpdateClassRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateStudentRequest>, CreateStudentRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdateStudentRequest>, UpdateStudentRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateTeacherRequest>, CreateTeacherRequestValidator>();
        builder.Services.AddScoped<IValidator<UpdateTeacherRequest>, UpdateTeacherRequestValidator>();
        builder.Services.AddScoped<IValidator<EnrollStudentRequest>, EnrollStudentRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateSessionRequest>, CreateSessionRequestValidator>();
        builder.Services.AddScoped<IValidator<RescheduleSessionRequest>, RescheduleSessionRequestValidator>();
        builder.Services.AddScoped<IValidator<CreateScheduleSeriesRequest>, CreateScheduleSeriesRequestValidator>();
        builder.Services.AddScoped<IValidator<StopScheduleSeriesRequest>, StopScheduleSeriesRequestValidator>();
        builder.Services.AddScoped<IValidator<ReplaceScheduleSeriesRequest>, ReplaceScheduleSeriesRequestValidator>();
        builder.Services.AddScoped<IValidator<TeachingVersionRequest>, TeachingVersionRequestValidator>();
        builder.Services.AddScoped<IValidator<SessionStatusChangeRequest>, SessionStatusChangeRequestValidator>();
        builder.Services.AddScoped<IValidator<SaveAttendanceDraftRequest>, SaveAttendanceDraftRequestValidator>();
        builder.Services.AddScoped<IValidator<CorrectAttendanceRequest>, CorrectAttendanceRequestValidator>();
        return builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
    }
}
