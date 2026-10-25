using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class ClassRegistrationStore(AcademicsDbContext db) : IClassRegistrationStore
{
    public Task<bool> CenterApprovedAsync(Guid centerId, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<bool>(
            db.Database.CurrentTransaction is null
                ? "SELECT EXISTS(SELECT 1 FROM organization.centers WHERE id = @center AND status = 'Approved') AS \"Value\""
                : "SELECT EXISTS(SELECT 1 FROM organization.centers WHERE id = @center AND status = 'Approved' FOR SHARE) AS \"Value\"",
            new NpgsqlParameter("center", centerId)).FirstAsync(cancellationToken);

    public async Task<(IReadOnlyList<TeachingClass> Items, int TotalCount)> OpenClassesAsync(Guid centerId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Classes.AsNoTracking().Where(x => x.CenterId == centerId && x.ArchivedAtUtc == null &&
            x.RegistrationOpen && x.EnrolledCount < x.MaxStudents);
        return (await query.OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken),
            await query.CountAsync(cancellationToken));
    }

    public async Task<bool> AreClassesOpenAsync(Guid centerId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken) =>
        await db.Classes.CountAsync(x => x.CenterId == centerId && classIds.Contains(x.Id) &&
            x.ArchivedAtUtc == null && x.RegistrationOpen && x.EnrolledCount < x.MaxStudents,
            cancellationToken) == classIds.Count;

    public async Task<IReadOnlyList<ClassApplication>> FindSubmissionAsync(Guid centerId, Guid userId,
        string key, CancellationToken cancellationToken) =>
        await db.ClassApplications.AsNoTracking().Where(x => x.CenterId == centerId && x.UserId == userId &&
            x.IdempotencyKey == key).OrderBy(x => x.ClassId).ToArrayAsync(cancellationToken);

    public Task<bool> HasAnyActiveChoiceAsync(Guid centerId, Guid userId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken) => db.ClassApplications.AnyAsync(x =>
            x.CenterId == centerId && x.UserId == userId && classIds.Contains(x.ClassId) &&
            x.Status != ClassApplicationStatus.Rejected, cancellationToken);

    public Task<ClassApplication?> FindApplicationForLockAsync(Guid centerId, Guid id,
        CancellationToken cancellationToken) => db.ClassApplications.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, cancellationToken);

    public Task<ClassApplication?> FindApplicationAsync(Guid centerId, Guid id,
        CancellationToken cancellationToken) => db.ClassApplications
        .SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, cancellationToken);

    public Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken cancellationToken) =>
        db.Students.SingleOrDefaultAsync(x => x.CenterId == centerId && x.UserId == userId, cancellationToken);

    public Task<bool> HasActiveEnrollmentAsync(Guid centerId, Guid userId, Guid classId,
        CancellationToken cancellationToken) =>
        (from enrollment in db.Enrollments
         join student in db.Students on enrollment.StudentId equals student.Id
         where enrollment.CenterId == centerId && enrollment.ClassId == classId &&
               enrollment.WithdrawnAtUtc == null && student.UserId == userId
         select enrollment.Id).AnyAsync(cancellationToken);

    public Task<bool> HasAnyActiveEnrollmentAsync(Guid centerId, Guid userId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken) =>
        (from enrollment in db.Enrollments
         join student in db.Students on enrollment.StudentId equals student.Id
         where enrollment.CenterId == centerId && classIds.Contains(enrollment.ClassId) &&
               enrollment.WithdrawnAtUtc == null && student.CenterId == centerId && student.UserId == userId
         select enrollment.Id).AnyAsync(cancellationToken);

    public async Task<(IReadOnlyList<ClassApplication> Items, int TotalCount)> ListMyApplicationsAsync(Guid centerId,
        Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.ClassApplications.AsNoTracking().Where(x => x.CenterId == centerId && x.UserId == userId);
        return (await query.OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken),
            await query.CountAsync(cancellationToken));
    }

    public async Task<(IReadOnlyList<ClassApplication> Items, int TotalCount)> ListApplicationsAsync(Guid centerId,
        ClassApplicationStatus? status, Guid? classId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.ClassApplications.AsNoTracking().Where(x => x.CenterId == centerId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (classId.HasValue) query = query.Where(x => x.ClassId == classId.Value);
        return (await query.OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(cancellationToken),
            await query.CountAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<StudentSessionDto>> ListMySessionsAsync(Guid centerId, Guid userId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        await (from session in db.Sessions.AsNoTracking()
               join enrollment in db.Enrollments.AsNoTracking() on session.ClassId equals enrollment.ClassId
               join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
               // A session belongs to the student only while the enrollment covered its start:
               // nothing before they joined, and past sessions stay visible after withdrawal.
               where session.CenterId == centerId && enrollment.CenterId == centerId &&
                     student.CenterId == centerId && student.UserId == userId &&
                     session.StartUtc >= enrollment.EnrolledAtUtc &&
                     (enrollment.WithdrawnAtUtc == null || session.StartUtc < enrollment.WithdrawnAtUtc) &&
                     session.StartUtc < toUtc && session.EndUtc > fromUtc
               orderby session.StartUtc
               // Cancelled and not-held sessions stay in the list with their reason (F1 labels).
               select new StudentSessionDto(session.Id, session.ClassId, session.TeacherId,
                   session.RoomId, session.StartUtc, session.EndUtc)
               {
                   Status = session.NotHeldAtUtc != null ? "NotHeld" : session.CancelledAtUtc != null ? "Cancelled" : "Scheduled",
                   StatusReason = session.NotHeldReason ?? session.CancellationReason,
                   IsRescheduled = session.RescheduledAtUtc != null,
                   RescheduleReason = session.RescheduleReason,
                   IsMakeup = session.MakeupForSessionId != null
               }).ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<OpenClassDto>> ListMyClassesAsync(Guid centerId, Guid userId,
        CancellationToken cancellationToken) =>
        await (from enrollment in db.Enrollments.AsNoTracking()
               join student in db.Students.AsNoTracking() on enrollment.StudentId equals student.Id
               join classEntity in db.Classes.AsNoTracking() on enrollment.ClassId equals classEntity.Id
               where enrollment.CenterId == centerId && student.CenterId == centerId &&
                     classEntity.CenterId == centerId && student.UserId == userId &&
                     student.ArchivedAtUtc == null && enrollment.WithdrawnAtUtc == null
               orderby classEntity.Code
               select new OpenClassDto(classEntity.Id, classEntity.SubjectId, classEntity.Code,
                   classEntity.Name, classEntity.MaxStudents, classEntity.EnrolledCount,
                   classEntity.RegistrationOpen))
            .ToArrayAsync(cancellationToken);

    public async Task<ITeachingGuard> BeginUserGuardAsync(Guid centerId, Guid userId, Guid? classId,
        CancellationToken cancellationToken)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (userId != Guid.Empty)
            {
                var key = $"application-user:{centerId}:{userId}";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            }
            if (classId.HasValue)
            {
                var key = $"class:{centerId}:{classId.Value}";
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            }
            return new RegistrationGuard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task AddAsync<T>(T entity, CancellationToken cancellationToken) where T : class =>
        await db.Set<T>().AddAsync(entity, cancellationToken);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex) { throw new TeachingPersistenceException("", true, ex); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation)
        { throw new TeachingPersistenceException(pg.ConstraintName ?? "", false, ex); }
    }

    private sealed class RegistrationGuard(IDbContextTransaction transaction) : ITeachingGuard
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
