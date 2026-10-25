using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class EnrollmentWorkflow(ITeachingStore store, IClock clock, IUserDirectory? users = null,
    ScheduleConflictChecker? conflicts = null, ScheduleChangeRecorder? recorder = null)
{
    public async Task<Result<EnrollmentDto>> EnrollAsync(Guid centerId, Guid classId,
        Guid studentId, CancellationToken token) =>
        (await EnrollAsync(centerId, classId, studentId, false, token)).Result;

    /// <summary>
    /// B5: a clash with the student's other classes in this center blocks the enrollment (the check is returned)
    /// unless <paramref name="acknowledgeConflicts"/> is set; clashes elsewhere are only counted.
    /// </summary>
    public Task<(Result<EnrollmentDto> Result, ScheduleCheckDto? Conflicts)> EnrollAsync(Guid centerId,
        Guid classId, Guid studentId, bool acknowledgeConflicts, CancellationToken token) =>
        EnrollAsync(centerId, classId, studentId, acknowledgeConflicts, null, token);

    /// <summary>
    /// A student with a login may also be enrolled by the center; that student is told through a notice and an email
    /// written in the same transaction.
    /// </summary>
    public async Task<(Result<EnrollmentDto> Result, ScheduleCheckDto? Conflicts)> EnrollAsync(Guid centerId,
        Guid classId, Guid studentId, bool acknowledgeConflicts, Guid? actorId, CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, classId, studentId, null, null, token);
        var teachingClass = await store.FindClassAsync(centerId, classId, token);
        if (teachingClass is null || !teachingClass.IsActive)
            return (Result<EnrollmentDto>.Failure(TeachingErrors.ClassUnavailable), null);
        var student = await store.FindStudentAsync(centerId, studentId, token);
        if (student is null || !student.IsActive)
            return (Result<EnrollmentDto>.Failure(TeachingErrors.StudentUnavailable), null);
        if (await store.FindActiveEnrollmentAsync(centerId, classId, studentId, token) is not null)
            return (Result<EnrollmentDto>.Failure(TeachingErrors.AlreadyEnrolled), null);
        if (teachingClass.EnrolledCount >= teachingClass.MaxStudents)
            return (Result<EnrollmentDto>.Failure(TeachingErrors.Full), null);

        var check = conflicts is null ? ScheduleCheckDto.Clear
            : await conflicts.CheckJoinAsync(centerId, classId, studentId, student.UserId, token);
        if (check.HasSameCenterConflicts && !acknowledgeConflicts)
            return (Result<EnrollmentDto>.Failure(TeachingErrors.StudentScheduleConflict), check);

        teachingClass.Enroll(clock.UtcNow);
        var enrollment = Enrollment.Create(centerId, classId, studentId, clock.UtcNow);
        await store.AddAsync(enrollment, token);
        if (student.UserId is { } userId && recorder is not null)
            await recorder.RecordEnrollmentAsync(centerId, classId, student.Id, userId, student.FullName, actorId,
                clock.UtcNow, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_enrollments_active_class_student")
        { return (Result<EnrollmentDto>.Failure(TeachingErrors.AlreadyEnrolled), null); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return (Result<EnrollmentDto>.Failure(TeachingErrors.ConcurrentChange), null); }
        await guard.CommitAsync(token);
        var dto = EnrollmentDto.From(enrollment);
        dto.OtherCenterConflictCount = check.OtherCenterConflictCount;
        return (Result<EnrollmentDto>.Success(dto), null);
    }

    public async Task<Result<EnrollmentDto>> WithdrawAsync(Guid centerId, Guid classId,
        Guid studentId, string reason, Guid actorId, CancellationToken token)
    {
        if (!Enrollment.IsValidReason(reason) || actorId == Guid.Empty)
            return Result<EnrollmentDto>.Failure(TeachingErrors.InvalidWithdrawalReason);
        await using var guard = await store.BeginGuardAsync(centerId, classId, studentId, null, null, token);
        var teachingClass = await store.FindClassAsync(centerId, classId, token);
        if (teachingClass is null) return Result<EnrollmentDto>.Failure(TeachingErrors.NotFound);
        var enrollment = await store.FindActiveEnrollmentAsync(centerId, classId, studentId, token);
        if (enrollment is null) return Result<EnrollmentDto>.Failure(TeachingErrors.NoActiveEnrollment);
        enrollment.Withdraw(clock.UtcNow, reason, actorId);
        teachingClass.Withdraw(clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<EnrollmentDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<EnrollmentDto>.Success(EnrollmentDto.From(enrollment));
    }

    public async Task<Result<TeachingPage<ClassStudentDto>>> ListAsync(Guid centerId, Guid classId,
        EnrollmentFilter status, int page, int pageSize, CancellationToken token)
    {
        if (await store.FindClassAsync(centerId, classId, token) is null)
            return Result<TeachingPage<ClassStudentDto>>.Failure(TeachingErrors.NotFound);
        var items = await store.ListClassStudentsAsync(centerId, classId, status, page, pageSize, token);
        if (users is not null)
        {
            var names = await users.GetDisplayNamesAsync(items.Items.Where(x => x.WithdrawnByUserId.HasValue)
                .Select(x => x.WithdrawnByUserId!.Value), token);
            foreach (var row in items.Items)
                row.WithdrawnByName = row.WithdrawnByUserId is { } id ? names.GetValueOrDefault(id) : null;
        }
        return Result<TeachingPage<ClassStudentDto>>.Success(new TeachingPage<ClassStudentDto>
        {
            Page = page, PageSize = pageSize, TotalCount = items.TotalCount, Items = items.Items.ToList()
        });
    }
}
