using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class PeopleCatalog(ITeachingStore store, IClock clock, IStudentLinkStore? links = null)
{
    /// <summary>
    /// Creates a student kept by the center. Leave <paramref name="code"/> empty to get the next HS-000001
    /// style code (D9); the parent name and mobile number are required (D5).
    /// </summary>
    public async Task<Result<StudentDto>> CreateStudentAsync(Guid centerId, string? code, string fullName,
        string parentName, string parentPhone, CancellationToken token)
    {
        var autoCode = string.IsNullOrWhiteSpace(code);
        if (centerId == Guid.Empty || (!autoCode && !TeachingClass.IsValidCode(code)) || !TeachingClass.IsValidName(fullName))
            return Result<StudentDto>.Failure(TeachingErrors.Invalid);
        if (!ParentContact.IsValidName(parentName)) return Result<StudentDto>.Failure(TeachingErrors.InvalidParentName);
        if (!ParentContact.TryNormalizePhone(parentPhone, out _)) return Result<StudentDto>.Failure(TeachingErrors.InvalidParentPhone);
        if (!autoCode && await store.StudentCodeExistsAsync(centerId, TeachingClass.NormalizeCode(code!), token))
            return Result<StudentDto>.Failure(TeachingErrors.CodeExists);
        var finalCode = autoCode ? await store.NextStudentCodeAsync(centerId, token) : code!;
        var value = Student.Create(centerId, finalCode, fullName, clock.UtcNow);
        value.UpdateProfile(fullName, parentName, parentPhone);
        await store.AddAsync(value, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_students_center_code")
        { return Result<StudentDto>.Failure(TeachingErrors.CodeExists); }
        return Result<StudentDto>.Success(StudentDto.From(value));
    }

    public async Task<Result<StudentDto>> UpdateStudentAsync(Guid centerId, Guid id, string fullName,
        string parentName, string parentPhone, int expectedVersion, CancellationToken token)
    {
        if (!TeachingClass.IsValidName(fullName) || expectedVersion < 0)
            return Result<StudentDto>.Failure(TeachingErrors.Invalid);
        if (!ParentContact.IsValidName(parentName)) return Result<StudentDto>.Failure(TeachingErrors.InvalidParentName);
        if (!ParentContact.TryNormalizePhone(parentPhone, out _)) return Result<StudentDto>.Failure(TeachingErrors.InvalidParentPhone);
        var value = await store.FindStudentAsync(centerId, id, token);
        if (value is null) return Result<StudentDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<StudentDto>.Failure(TeachingErrors.StudentUnavailable);
        value.UpdateProfile(fullName, parentName, parentPhone);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<StudentDto>.Success(StudentDto.From(value));
    }

    public async Task<Result<StudentDto>> ArchiveStudentAsync(Guid centerId, Guid id,
        int expectedVersion, CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, null, id, null, null, token);
        var value = await store.FindStudentAsync(centerId, id, token);
        if (value is null) return Result<StudentDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<StudentDto>.Failure(TeachingErrors.StudentUnavailable);
        if (await store.StudentHasActiveEnrollmentAsync(centerId, id, token))
            return Result<StudentDto>.Failure(TeachingErrors.StudentHasEnrollments);
        value.Archive(clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<StudentDto>.Success(StudentDto.From(value));
    }

    public async Task<Result<TeacherDto>> CreateTeacherAsync(Guid centerId, string code, string fullName, CancellationToken token)
    {
        if (centerId == Guid.Empty || !TeachingClass.IsValidCode(code) || !TeachingClass.IsValidName(fullName))
            return Result<TeacherDto>.Failure(TeachingErrors.Invalid);
        if (await store.TeacherCodeExistsAsync(centerId, TeachingClass.NormalizeCode(code), token))
            return Result<TeacherDto>.Failure(TeachingErrors.CodeExists);
        var value = Teacher.Create(centerId, code, fullName, clock.UtcNow);
        await store.AddAsync(value, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_teachers_center_code")
        { return Result<TeacherDto>.Failure(TeachingErrors.CodeExists); }
        return Result<TeacherDto>.Success(TeacherDto.From(value));
    }

    public async Task<Result<TeacherDto>> ArchiveTeacherAsync(Guid centerId, Guid id,
        int expectedVersion, CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, null, null, id, null, token);
        var value = await store.FindTeacherAsync(centerId, id, token);
        if (value is null) return Result<TeacherDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<TeacherDto>.Failure(TeachingErrors.TeacherUnavailable);
        if (await store.TeacherHasFutureSessionsAsync(centerId, id, clock.UtcNow, token))
            return Result<TeacherDto>.Failure(TeachingErrors.HasFutureSessions);
        value.Archive(clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<TeacherDto>.Success(TeacherDto.From(value));
    }

    public async Task<Result<TeacherDto>> UpdateTeacherAsync(Guid centerId, Guid id, string fullName,
        int expectedVersion, CancellationToken token)
    {
        if (!TeachingClass.IsValidName(fullName) || expectedVersion < 0)
            return Result<TeacherDto>.Failure(TeachingErrors.Invalid);
        var value = await store.FindTeacherAsync(centerId, id, token);
        if (value is null) return Result<TeacherDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<TeacherDto>.Failure(TeachingErrors.TeacherUnavailable);
        value.Rename(fullName);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<TeacherDto>.Success(TeacherDto.From(value));
    }

    public async Task<Result<StudentDto>> ReactivateStudentAsync(Guid centerId, Guid id,
        int expectedVersion, CancellationToken token)
    {
        var value = await store.FindStudentAsync(centerId, id, token);
        if (value is null) return Result<StudentDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange);
        if (value.IsActive) return Result<StudentDto>.Failure(TeachingErrors.AlreadyActive);
        if (value.MergedIntoStudentId.HasValue) return Result<StudentDto>.Failure(StudentLinkErrors.ProfileUnavailable);
        value.Reactivate();
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<StudentDto>.Success(StudentDto.From(value));
    }

    public async Task<Result<TeacherDto>> ReactivateTeacherAsync(Guid centerId, Guid id,
        int expectedVersion, CancellationToken token)
    {
        var value = await store.FindTeacherAsync(centerId, id, token);
        if (value is null) return Result<TeacherDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange);
        if (value.IsActive) return Result<TeacherDto>.Failure(TeachingErrors.AlreadyActive);
        value.Reactivate();
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<TeacherDto>.Success(TeacherDto.From(value));
    }

    public Task<TeachingPage<StudentDto>> ListStudentsAsync(Guid centerId, bool includeArchived,
        int page, int pageSize, CancellationToken token) =>
        ListStudentsAsync(centerId, includeArchived, null, page, pageSize, token);

    public async Task<TeachingPage<StudentDto>> ListStudentsAsync(Guid centerId, bool includeArchived, string? search,
        int page, int pageSize, CancellationToken token)
    {
        var (items, totalCount) = await store.ListStudentsAsync(centerId, includeArchived, search, page, pageSize, token);
        return new TeachingPage<StudentDto> { Page = page, PageSize = pageSize, TotalCount = totalCount,
            Items = (await DecorateAsync(centerId, items, token)).ToList() };
    }

    public Task<TeachingPage<TeacherDto>> ListTeachersAsync(Guid centerId, bool includeArchived,
        int page, int pageSize, CancellationToken token) =>
        ListTeachersAsync(centerId, includeArchived, null, page, pageSize, token);

    public async Task<TeachingPage<TeacherDto>> ListTeachersAsync(Guid centerId, bool includeArchived, string? search,
        int page, int pageSize, CancellationToken token)
    {
        var (items, totalCount) = await store.ListTeachersAsync(centerId, includeArchived, search, page, pageSize, token);
        return new TeachingPage<TeacherDto> { Page = page, PageSize = pageSize, TotalCount = totalCount,
            Items = items.Select(TeacherDto.From).ToList() };
    }
    public async Task<Result<StudentDto>> GetStudentAsync(Guid centerId, Guid id, CancellationToken token)
    {
        var value = await store.FindStudentAsync(centerId, id, token);
        return value is null ? Result<StudentDto>.Failure(TeachingErrors.NotFound)
            : Result<StudentDto>.Success((await DecorateAsync(centerId, [value], token))[0]);
    }
    /// <summary>Adds the duplicate candidate's code/name and the latest activation link state (M3).</summary>
    private async Task<IReadOnlyList<StudentDto>> DecorateAsync(Guid centerId, IReadOnlyList<Student> items,
        CancellationToken token)
    {
        var dtos = items.Select(StudentDto.From).ToList();
        if (links is null || dtos.Count == 0) return dtos;
        var latest = await links.LatestActivationLinksAsync(centerId,
            items.Where(x => !x.UserId.HasValue).Select(x => x.Id).ToArray(), token);
        var now = clock.UtcNow;
        foreach (var dto in dtos)
        {
            dto.Activation = StudentAccessWorkflow.Status(latest.GetValueOrDefault(dto.Id), now);
            if (dto.DuplicateCandidateId is not { } other) continue;
            var candidate = await links.FindStudentAsync(centerId, other, token);
            dto.DuplicateCandidateCode = candidate?.Code;
            dto.DuplicateCandidateName = candidate?.FullName;
        }
        return dtos;
    }

    public async Task<Result<TeacherDto>> GetTeacherAsync(Guid centerId, Guid id, CancellationToken token)
    {
        var value = await store.FindTeacherAsync(centerId, id, token);
        return value is null ? Result<TeacherDto>.Failure(TeachingErrors.NotFound) : Result<TeacherDto>.Success(TeacherDto.From(value));
    }
}
