using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class ClassCatalog(ITeachingStore store, ISubjectClassGuard subjectGuard, IClock clock)
{
    public async Task<Result<ClassDto>> CreateAsync(Guid centerId, Guid subjectId, string code,
        string name, int maxStudents, Guid actorId, CancellationToken token)
    {
        if (actorId == Guid.Empty || !TeachingClass.AreDetailsValid(code, name, maxStudents))
            return Result<ClassDto>.Failure(TeachingErrors.Invalid);
        await using var guard = await subjectGuard.BeginAsync(centerId, subjectId, token);
        if (!await store.SubjectIsActiveAsync(centerId, subjectId, token))
            return Result<ClassDto>.Failure(TeachingErrors.SubjectUnavailable);
        if (await store.ClassCodeExistsAsync(centerId, TeachingClass.NormalizeCode(code), token))
            return Result<ClassDto>.Failure(TeachingErrors.CodeExists);
        var value = TeachingClass.Create(centerId, subjectId, code, name, maxStudents, actorId, clock.UtcNow);
        await store.AddAsync(value, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_classes_center_code")
        { return Result<ClassDto>.Failure(TeachingErrors.CodeExists); }
        await guard.CommitAsync(token);
        return Result<ClassDto>.Success(ClassDto.From(value));
    }

    public async Task<Result<ClassDto>> UpdateAsync(Guid centerId, Guid classId, string name,
        int maxStudents, int expectedVersion, CancellationToken token)
    {
        if (!TeachingClass.IsValidName(name) || maxStudents is < 1 or > 500 || expectedVersion < 0)
            return Result<ClassDto>.Failure(TeachingErrors.Invalid);
        var value = await store.FindClassAsync(centerId, classId, token);
        if (value is null) return Result<ClassDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<ClassDto>.Failure(TeachingErrors.ClassUnavailable);
        if (maxStudents < value.EnrolledCount) return Result<ClassDto>.Failure(TeachingErrors.Full);
        value.Update(name, maxStudents, clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<ClassDto>.Success(ClassDto.From(value));
    }

    public async Task<Result<ClassDto>> ArchiveAsync(Guid centerId, Guid classId,
        int expectedVersion, CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, classId, null, null, null, token);
        var value = await store.FindClassAsync(centerId, classId, token);
        if (value is null) return Result<ClassDto>.Failure(TeachingErrors.NotFound);
        if (value.Version != expectedVersion) return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!value.IsActive) return Result<ClassDto>.Failure(TeachingErrors.ClassUnavailable);
        if (value.EnrolledCount > 0) return Result<ClassDto>.Failure(TeachingErrors.HasEnrollments);
        if (await store.ClassHasFutureSessionsAsync(centerId, classId, clock.UtcNow, token))
            return Result<ClassDto>.Failure(TeachingErrors.HasFutureSessions);
        value.Archive(clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<ClassDto>.Success(ClassDto.From(value));
    }

    public async Task<Result<ClassDto>> GetAsync(Guid centerId, Guid classId, CancellationToken token)
    {
        var value = await store.FindClassAsync(centerId, classId, token);
        return value is null ? Result<ClassDto>.Failure(TeachingErrors.NotFound) : Result<ClassDto>.Success(ClassDto.From(value));
    }

    public async Task<Result<ClassDto>> ReactivateAsync(Guid centerId, Guid classId,
        int expectedVersion, CancellationToken token)
    {
        var value = await store.FindClassAsync(centerId, classId, token);
        if (value is null) return Result<ClassDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await subjectGuard.BeginAsync(centerId, value.SubjectId, token);
        if (value.Version != expectedVersion) return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange);
        if (value.IsActive) return Result<ClassDto>.Failure(TeachingErrors.AlreadyActive);
        if (!await store.SubjectIsActiveAsync(centerId, value.SubjectId, token))
            return Result<ClassDto>.Failure(TeachingErrors.SubjectUnavailable);
        value.Reactivate(clock.UtcNow);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<ClassDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<ClassDto>.Success(ClassDto.From(value));
    }

    public Task<TeachingPage<ClassDto>> ListAsync(Guid centerId, bool includeArchived,
        int page, int pageSize, CancellationToken token) =>
        ListAsync(centerId, includeArchived, null, page, pageSize, token);

    public async Task<TeachingPage<ClassDto>> ListAsync(Guid centerId, bool includeArchived, string? search,
        int page, int pageSize, CancellationToken token)
    {
        var (items, totalCount) = await store.ListClassesAsync(centerId, includeArchived, search, page, pageSize, token);
        return new TeachingPage<ClassDto> { Page = page, PageSize = pageSize, TotalCount = totalCount,
            Items = items.Select(ClassDto.From).ToList() };
    }
}
