using System.Security.Cryptography;
using System.Text;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record OpenClassDto(Guid Id, Guid SubjectId, string Code, string Name, int MaxStudents,
    int EnrolledCount, bool RegistrationOpen);
public sealed record ClassApplicationDto(Guid Id, Guid SubmissionId, Guid ClassId,
    string FullName, string Status, DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc, string? ReviewReason)
{
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? ParentName { get; init; }
    public string? ParentPhone { get; init; }
    public string? ReviewedByName { get; init; }
    /// <summary>After approval: sessions of the class that clash with another center (count only, B5).</summary>
    public int? OtherCenterConflictCount { get; init; }
}
public sealed record SubmittedClassApplications(IReadOnlyList<ClassApplicationDto> Items, bool Replayed);
public sealed record StudentSessionDto(Guid Id, Guid ClassId, Guid TeacherId, Guid RoomId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    /// <summary>Scheduled, Cancelled or NotHeld; cancelled sessions stay visible with their reason.</summary>
    public string Status { get; init; } = nameof(TeachingSessionStatus.Scheduled);
    public string? StatusReason { get; init; }
    public bool IsRescheduled { get; init; }
    public string? RescheduleReason { get; init; }
    public bool IsMakeup { get; init; }
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? TeacherName { get; init; }
    public string? RoomName { get; init; }
}

public interface IClassRegistrationStore
{
    Task<bool> CenterApprovedAsync(Guid centerId, CancellationToken cancellationToken);
    Task<(IReadOnlyList<TeachingClass> Items, int TotalCount)> OpenClassesAsync(Guid centerId,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<bool> AreClassesOpenAsync(Guid centerId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ClassApplication>> FindSubmissionAsync(Guid centerId, Guid userId,
        string key, CancellationToken cancellationToken);
    Task<bool> HasAnyActiveChoiceAsync(Guid centerId, Guid userId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken);
    Task<ClassApplication?> FindApplicationAsync(Guid centerId, Guid id, CancellationToken cancellationToken);
    Task<ClassApplication?> FindApplicationForLockAsync(Guid centerId, Guid id, CancellationToken cancellationToken);
    Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken cancellationToken);
    Task<bool> HasActiveEnrollmentAsync(Guid centerId, Guid userId, Guid classId, CancellationToken cancellationToken);
    Task<bool> HasAnyActiveEnrollmentAsync(Guid centerId, Guid userId, IReadOnlyList<Guid> classIds,
        CancellationToken cancellationToken);
    Task<(IReadOnlyList<ClassApplication> Items, int TotalCount)> ListMyApplicationsAsync(Guid centerId,
        Guid userId, int page, int pageSize, CancellationToken cancellationToken);
    Task<(IReadOnlyList<ClassApplication> Items, int TotalCount)> ListApplicationsAsync(Guid centerId,
        ClassApplicationStatus? status, Guid? classId, int page, int pageSize, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentSessionDto>> ListMySessionsAsync(Guid centerId, Guid userId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<OpenClassDto>> ListMyClassesAsync(Guid centerId, Guid userId,
        CancellationToken cancellationToken);
    Task<ITeachingGuard> BeginUserGuardAsync(Guid centerId, Guid userId, Guid? classId,
        CancellationToken cancellationToken);
    Task AddAsync<T>(T entity, CancellationToken cancellationToken) where T : class;
    Task SaveAsync(CancellationToken cancellationToken);
}

public static class ClassRegistrationErrors
{
    public static readonly Error Invalid = new("invalid_class_registration", "The class registration request is invalid.");
    public static readonly Error InvalidName = new("validation_error", "Full name must contain 2–100 characters without control characters.", "fullName");
    public static readonly Error InvalidParentName = new("validation_error", "Parent name must contain 2–100 characters.", "parentName");
    public static readonly Error InvalidParentPhone = new("validation_error", "Parent phone must be a Vietnamese mobile number.", "parentPhone");
    public static readonly Error InvalidClasses = new("validation_error", "Select 1–10 distinct, non-empty class IDs.", "classIds");
    public static readonly Error InvalidKey = new("validation_error", "Provide a valid UUID in the Idempotency-Key header.", "Idempotency-Key");
    public static readonly Error InvalidReason = new("validation_error", "Provide a rejection reason of 1–500 characters.", "reason");
    public static readonly Error InvalidSessionRange = new("validation_error", "Provide an increasing date range of at most 93 days.", "toUtc");
    public static readonly Error InvalidStatus = new("validation_error", "Status must be Pending, Approved or Rejected.", "status");
    public static readonly Error CenterUnavailable = new("center_unavailable", "The center is not approved.");
    public static readonly Error ClassClosed = new("class_registration_closed", "A selected class is not open for registration.", "classIds");
    public static readonly Error AlreadyApplied = new("class_already_applied", "A pending or approved application already exists for this class.", "classIds");
    public static readonly Error KeyReused = new("idempotency_key_reused", "This idempotency key belongs to a different request.", "Idempotency-Key");
    public static readonly Error NotFound = new("class_application_not_found", "The application was not found in this center.");
    public static readonly Error NotPending = new("class_application_not_pending", "This application has already been reviewed.");
    public static readonly Error StudentArchived = new("student_archived", "The linked student is archived; reactivate it before approval.");
    public static readonly Error Full = new("class_full", "The class is full; this application remains pending.");
    public static readonly Error AlreadyEnrolled = new("student_already_enrolled", "This student is already enrolled in this class.");
    public static readonly Error ConcurrentChange = new("class_registration_concurrent_change", "The registration changed. Reload and retry.");
}

public sealed class ClassRegistrationWorkflow(IClassRegistrationStore store, ITeachingStore teaching, IClock clock,
    ITeachingNameReader? names = null, TutorManagement.Modules.IdentityAccess.Contracts.Members.IUserDirectory? users = null,
    ScheduleConflictChecker? conflicts = null, IStudentLinkStore? links = null, ClassApplicationMail? mail = null)
{
    public async Task<Result<TeachingPage<OpenClassDto>>> OpenClassesAsync(Guid centerId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!await store.CenterApprovedAsync(centerId, cancellationToken))
            return Result<TeachingPage<OpenClassDto>>.Failure(ClassRegistrationErrors.CenterUnavailable);
        var classes = await store.OpenClassesAsync(centerId, page, pageSize, cancellationToken);
        return Result<TeachingPage<OpenClassDto>>.Success(new TeachingPage<OpenClassDto>
        {
            Page = page, PageSize = pageSize, TotalCount = classes.TotalCount,
            Items = classes.Items.Select(x => new OpenClassDto(x.Id, x.SubjectId, x.Code, x.Name,
                x.MaxStudents, x.EnrolledCount, x.RegistrationOpen)).ToList()
        });
    }

    public async Task<Result<SubmittedClassApplications>> SubmitAsync(Guid centerId, Guid userId,
        string fullName, string parentName, string parentPhone, IReadOnlyList<Guid> classIds, string key,
        CancellationToken cancellationToken)
    {
        if (centerId == Guid.Empty || userId == Guid.Empty)
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.Invalid);
        if (!TeachingClass.IsValidName(fullName))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.InvalidName);
        if (!ParentContact.IsValidName(parentName))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.InvalidParentName);
        if (!ParentContact.TryNormalizePhone(parentPhone, out var normalizedPhone))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.InvalidParentPhone);
        if (classIds is not { Count: >= 1 and <= 10 } || classIds.Any(x => x == Guid.Empty) ||
            classIds.Distinct().Count() != classIds.Count)
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.InvalidClasses);
        if (string.IsNullOrWhiteSpace(key) || key.Length > 80 || !Guid.TryParse(key, out _))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.InvalidKey);

        key = Guid.Parse(key).ToString("D");
        var sorted = classIds.Order().ToArray();
        var normalizedName = TeachingClass.NormalizeName(fullName);
        var normalizedParent = TeachingClass.NormalizeName(parentName);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            normalizedName + "|" + normalizedParent + "|" + normalizedPhone + "|" + string.Join('|', sorted))));
        await using var guard = await store.BeginUserGuardAsync(centerId, userId, null, cancellationToken);
        var previous = await store.FindSubmissionAsync(centerId, userId, key, cancellationToken);
        if (previous.Count > 0)
            return previous.Count == sorted.Length && previous.All(x => x.RequestHash == hash)
                ? Result<SubmittedClassApplications>.Success(new SubmittedClassApplications(
                    previous.Select(ToDto).ToArray(), true))
                : Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.KeyReused);

        if (!await store.CenterApprovedAsync(centerId, cancellationToken))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.CenterUnavailable);
        if (!await store.AreClassesOpenAsync(centerId, sorted, cancellationToken))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.ClassClosed);
        if (await store.HasAnyActiveChoiceAsync(centerId, userId, sorted, cancellationToken))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.AlreadyApplied);
        if (await store.HasAnyActiveEnrollmentAsync(centerId, userId, sorted, cancellationToken))
            return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.AlreadyEnrolled);

        var submissionId = Guid.NewGuid();
        var items = sorted.Select(classId => ClassApplication.Create(centerId, classId, userId,
            submissionId, key, hash, normalizedName, clock.UtcNow, normalizedParent, normalizedPhone)).ToArray();
        foreach (var item in items) await store.AddAsync(item, cancellationToken);
        try { await store.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_class_applications_active_choice")
        { return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.AlreadyApplied); }
        catch (TeachingPersistenceException)
        { return Result<SubmittedClassApplications>.Failure(ClassRegistrationErrors.ConcurrentChange); }
        await guard.CommitAsync(cancellationToken);
        return Result<SubmittedClassApplications>.Success(new SubmittedClassApplications(
            items.Select(ToDto).ToArray(), false));
    }

    public async Task<Result<ClassApplicationDto>> ApproveAsync(Guid centerId, Guid applicationId,
        Guid reviewerId, CancellationToken cancellationToken) =>
        (await ApproveAsync(centerId, applicationId, reviewerId, false, cancellationToken)).Result;

    /// <summary>
    /// B5: approval is refused while the student has other classes of this center at the same time (the check is
    /// returned) unless <paramref name="acknowledgeConflicts"/> is set; clashes elsewhere are only counted.
    /// </summary>
    public async Task<(Result<ClassApplicationDto> Result, ScheduleCheckDto? Conflicts)> ApproveAsync(Guid centerId,
        Guid applicationId, Guid reviewerId, bool acknowledgeConflicts, CancellationToken cancellationToken)
    {
        // The preliminary read supplies the lock key only; every decision uses a fresh read after locking.
        var preliminary = await store.FindApplicationForLockAsync(centerId, applicationId, cancellationToken);
        if (preliminary is null) return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotFound), null);
        await using var guard = await store.BeginUserGuardAsync(centerId, preliminary.UserId,
            preliminary.ClassId, cancellationToken);
        var item = await store.FindApplicationAsync(centerId, applicationId, cancellationToken);
        if (item is null) return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotFound), null);
        if (item.Status == ClassApplicationStatus.Approved)
            return (Result<ClassApplicationDto>.Success(ToDto(item)), null);
        if (item.Status != ClassApplicationStatus.Pending)
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotPending), null);
        if (!await store.CenterApprovedAsync(centerId, cancellationToken))
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.CenterUnavailable), null);
        var classEntity = await teaching.FindClassAsync(centerId, item.ClassId, cancellationToken);
        if (classEntity is null || !classEntity.IsActive)
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.ClassClosed), null);
        if (classEntity.EnrolledCount >= classEntity.MaxStudents)
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.Full), null);
        if (await store.HasActiveEnrollmentAsync(centerId, item.UserId, item.ClassId, cancellationToken))
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.AlreadyEnrolled), null);

        var student = await store.FindStudentForUserAsync(centerId, item.UserId, cancellationToken);
        if (student is { IsActive: false })
            return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.StudentArchived), null);
        var check = conflicts is null ? ScheduleCheckDto.Clear
            : await conflicts.CheckJoinAsync(centerId, item.ClassId, student?.Id ?? Guid.Empty, item.UserId, cancellationToken);
        if (check.HasSameCenterConflicts && !acknowledgeConflicts)
            return (Result<ClassApplicationDto>.Failure(TeachingErrors.StudentScheduleConflict), check);
        if (student is null)
        {
            var code = await teaching.NextStudentCodeAsync(centerId, cancellationToken);
            student = Student.CreateForUser(centerId, item.UserId, code, item.FullName, clock.UtcNow);
            await store.AddAsync(student, cancellationToken);
            // M3: a hint for the center only ("Có thể trùng HS-…"); nothing is merged or blocked.
            if (links is not null &&
                await links.FindUnlinkedNamesakeAsync(centerId, student.FullName, student.Id, cancellationToken) is { } namesake)
                student.FlagDuplicate(namesake.Id, StudentDuplicateReason.SameName);
        }
        student.AdoptParentContactIfMissing(item.ParentName, item.ParentPhone);
        classEntity.Enroll(clock.UtcNow);
        item.Approve(reviewerId, clock.UtcNow);
        await store.AddAsync(Enrollment.Create(centerId, item.ClassId, student.Id, clock.UtcNow), cancellationToken);
        try { await store.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_enrollments_active_class_student")
        { return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.AlreadyEnrolled), null); }
        catch (TeachingPersistenceException)
        { return (Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.ConcurrentChange), null); }
        if (mail is not null)
            await mail.ApprovedAsync(centerId, item.ClassId, item.UserId, item.FullName, check.OtherCenterConflictCount,
                cancellationToken);
        await guard.CommitAsync(cancellationToken);
        return (Result<ClassApplicationDto>.Success(ToDto(item) with
        {
            OtherCenterConflictCount = check.OtherCenterConflictCount
        }), null);
    }

    public async Task<Result<ClassApplicationDto>> RejectAsync(Guid centerId, Guid applicationId,
        Guid reviewerId, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500)
            return Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.InvalidReason);
        var preliminary = await store.FindApplicationForLockAsync(centerId, applicationId, cancellationToken);
        if (preliminary is null) return Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotFound);
        await using var guard = await store.BeginUserGuardAsync(centerId, preliminary.UserId,
            preliminary.ClassId, cancellationToken);
        var item = await store.FindApplicationAsync(centerId, applicationId, cancellationToken);
        if (item is null) return Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotFound);
        if (item.Status == ClassApplicationStatus.Rejected &&
            string.Equals(item.ReviewReason, reason.Trim(), StringComparison.Ordinal))
            return Result<ClassApplicationDto>.Success(ToDto(item));
        if (item.Status != ClassApplicationStatus.Pending)
            return Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.NotPending);
        item.Reject(reviewerId, reason, clock.UtcNow);
        try { await store.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException)
        { return Result<ClassApplicationDto>.Failure(ClassRegistrationErrors.ConcurrentChange); }
        if (mail is not null)
            await mail.RejectedAsync(centerId, item.ClassId, item.UserId, item.FullName, reason, cancellationToken);
        await guard.CommitAsync(cancellationToken);
        return Result<ClassApplicationDto>.Success(ToDto(item));
    }

    public async Task<TeachingPage<ClassApplicationDto>> MyApplicationsAsync(Guid centerId, Guid userId,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var items = await store.ListMyApplicationsAsync(centerId, userId, page, pageSize, cancellationToken);
        return await ToNamedPageAsync(centerId, items, page, pageSize, cancellationToken);
    }

    public Task<TeachingPage<ClassApplicationDto>> ListApplicationsAsync(Guid centerId,
        ClassApplicationStatus? status, int page, int pageSize, CancellationToken cancellationToken) =>
        ListApplicationsAsync(centerId, status, null, page, pageSize, cancellationToken);

    public async Task<TeachingPage<ClassApplicationDto>> ListApplicationsAsync(Guid centerId,
        ClassApplicationStatus? status, Guid? classId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var items = await store.ListApplicationsAsync(centerId, status, classId, page, pageSize, cancellationToken);
        return await ToNamedPageAsync(centerId, items, page, pageSize, cancellationToken);
    }

    public Task<IReadOnlyList<OpenClassDto>> MyClassesAsync(Guid centerId, Guid userId,
        CancellationToken cancellationToken) => store.ListMyClassesAsync(centerId, userId, cancellationToken);

    public async Task<IReadOnlyList<StudentSessionDto>> MySessionsAsync(Guid centerId, Guid userId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        var items = await store.ListMySessionsAsync(centerId, userId, fromUtc, toUtc, cancellationToken);
        if (names is null || items.Count == 0) return items;
        var lookup = await names.ReadAsync(centerId, items.Select(x => x.ClassId),
            items.Select(x => x.TeacherId), items.Select(x => x.RoomId), cancellationToken);
        return items.Select(x => x.WithNames(lookup)).ToArray();
    }

    public async Task<Result<ClassDto>> SetRegistrationAsync(Guid centerId, Guid classId,
        bool open, int expectedVersion, CancellationToken cancellationToken)
    {
        await using var guard = await store.BeginUserGuardAsync(centerId, Guid.Empty, classId, cancellationToken);
        var classEntity = await teaching.FindClassAsync(centerId, classId, cancellationToken);
        if (classEntity is null || !classEntity.IsActive)
            return Result<ClassDto>.Failure(ClassRegistrationErrors.ClassClosed);
        if (classEntity.Version != expectedVersion)
            return Result<ClassDto>.Failure(ClassRegistrationErrors.ConcurrentChange);
        classEntity.SetRegistrationOpen(open, clock.UtcNow);
        try { await store.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException)
        { return Result<ClassDto>.Failure(ClassRegistrationErrors.ConcurrentChange); }
        await guard.CommitAsync(cancellationToken);
        return Result<ClassDto>.Success(ClassDto.From(classEntity));
    }

    private static ClassApplicationDto ToDto(ClassApplication item) => new(item.Id, item.SubmissionId,
        item.ClassId, item.FullName, item.Status.ToString(),
        item.SubmittedAtUtc, item.ReviewedAtUtc, item.ReviewReason)
    {
        ParentName = item.ParentName, ParentPhone = item.ParentPhone
    };

    private async Task<TeachingPage<ClassApplicationDto>> ToNamedPageAsync(Guid centerId,
        (IReadOnlyList<ClassApplication> Items, int TotalCount) result, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var dto = ToPage(result, page, pageSize);
        if (dto.Items.Count == 0) return dto;
        if (names is not null)
        {
            var lookup = await names.ReadAsync(centerId, dto.Items.Select(x => x.ClassId), [], [], cancellationToken);
            dto.Items = dto.Items.Select(x => x.WithNames(lookup)).ToList();
        }
        if (users is not null)
        {
            var reviewers = result.Items.Where(x => x.ReviewedByUserId.HasValue)
                .ToDictionary(x => x.Id, x => x.ReviewedByUserId!.Value);
            var people = await users.GetDisplayNamesAsync(reviewers.Values, cancellationToken);
            dto.Items = dto.Items.Select(x => reviewers.TryGetValue(x.Id, out var reviewer)
                ? x with { ReviewedByName = people.GetValueOrDefault(reviewer) } : x).ToList();
        }
        return dto;
    }

    private static TeachingPage<ClassApplicationDto> ToPage(
        (IReadOnlyList<ClassApplication> Items, int TotalCount) result, int page, int pageSize) => new()
    {
        Page = page, PageSize = pageSize, TotalCount = result.TotalCount,
        Items = result.Items.Select(ToDto).ToList()
    };
}
