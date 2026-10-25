using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Application.Subjects.ArchiveSubject;
using TutorManagement.Modules.Academics.Application.Subjects.CreateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.GetSubject;
using TutorManagement.Modules.Academics.Application.Subjects.ListSubjects;
using TutorManagement.Modules.Academics.Application.Subjects.ReactivateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.UpdateSubject;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Academics.Application.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AcademicsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AcademicsDbContext.Schema)));

        services.AddScoped<ISubjectRepository, SubjectRepository>();
        services.AddScoped<ISubjectClassGuard, SubjectClassGuard>();
        services.AddScoped<ITeachingStore, TeachingStore>();
        services.AddScoped<IAttendanceStore, AttendanceStore>();
        services.AddScoped<ITeachingNameReader, TeachingNameReader>();
        services.AddScoped<AttendanceWorkflow>();
        services.AddScoped<IClassRegistrationStore, ClassRegistrationStore>();
        services.AddScoped<ClassRegistrationWorkflow>();
        services.AddScoped<ClassCatalog>();
        services.AddScoped<PeopleCatalog>();
        services.AddScoped<TeacherAccountWorkflow>();
        services.AddScoped<EnrollmentWorkflow>();
        services.AddScoped<SessionScheduler>();
        services.AddScoped<RecurringScheduleWorkflow>();
        services.AddScoped<IStudentTimetableStore, StudentTimetableStore>();
        services.AddScoped<IPersonalScheduleStore, PersonalScheduleStore>();
        services.AddScoped<IScheduleChangeStore, ScheduleChangeStore>();
        services.AddScoped<IStudentRequestStore, StudentRequestStore>();
        services.AddScoped<ScheduleConflictChecker>();
        services.AddScoped<ScheduleChangeRecorder>();
        services.AddScoped<ClassApplicationMail>();
        services.AddScoped<ScheduleChangeQueries>();
        services.AddScoped<PersonalScheduleWorkflow>();
        services.AddScoped<StudentRequestWorkflow>();
        services.AddScoped<ICourseDirectoryStore, CourseDirectoryStore>();
        services.AddScoped<CourseDirectory>();
        services.AddScoped<IStudentLinkStore, StudentLinkStore>();
        services.AddScoped<StudentAccessWorkflow>();
        services.AddScoped<StudentJoinWorkflow>();
        services.AddScoped<IAcademicsUnitOfWork>(provider => provider.GetRequiredService<AcademicsDbContext>());
        services.AddScoped<ICommandHandler<CreateSubjectCommand, SubjectDto>, CreateSubjectCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateSubjectCommand, SubjectDto>, UpdateSubjectCommandHandler>();
        services.AddScoped<ICommandHandler<ArchiveSubjectCommand, SubjectDto>, ArchiveSubjectCommandHandler>();
        services.AddScoped<ICommandHandler<ReactivateSubjectCommand, SubjectDto>, ReactivateSubjectCommandHandler>();
        services.AddScoped<IQueryHandler<GetSubjectQuery, SubjectDto>, GetSubjectQueryHandler>();
        services.AddScoped<IQueryHandler<ListSubjectsQuery, List<SubjectDto>>, ListSubjectsQueryHandler>();

        return services;
    }
}
