using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence;

public sealed class AcademicsDbContext(DbContextOptions<AcademicsDbContext> options)
    : DbContext(options), IAcademicsUnitOfWork
{
    public const string Schema = "academics";

    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<TeachingClass> Classes => Set<TeachingClass>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<ClassApplication> ClassApplications => Set<ClassApplication>();
    public DbSet<TeachingSession> Sessions => Set<TeachingSession>();
    public DbSet<ScheduleSeries> ScheduleSeries => Set<ScheduleSeries>();
    public DbSet<SessionAttendance> AttendanceSheets => Set<SessionAttendance>();
    public DbSet<AttendanceEntry> AttendanceEntries => Set<AttendanceEntry>();
    public DbSet<AttendanceRevision> AttendanceRevisions => Set<AttendanceRevision>();
    public DbSet<ScheduleChange> ScheduleChanges => Set<ScheduleChange>();
    public DbSet<ScheduleNotice> ScheduleNotices => Set<ScheduleNotice>();
    public DbSet<StudentRequest> StudentRequests => Set<StudentRequest>();
    public DbSet<StudentActivationLink> StudentActivationLinks => Set<StudentActivationLink>();
    public DbSet<ClassInviteLink> ClassInviteLinks => Set<ClassInviteLink>();
    public DbSet<ClassInviteAttempt> ClassInviteAttempts => Set<ClassInviteAttempt>();
    public DbSet<ClassInviteJoin> ClassInviteJoins => Set<ClassInviteJoin>();

    async Task<int> IAcademicsUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            throw new SubjectDuplicateException(postgres.ConstraintName ?? string.Empty, exception);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new SubjectConcurrencyException(exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademicsDbContext).Assembly);
    }
}
