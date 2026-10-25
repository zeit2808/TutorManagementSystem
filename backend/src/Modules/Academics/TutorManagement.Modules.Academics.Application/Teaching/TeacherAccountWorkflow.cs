using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record MyTeachingDto(TeacherDto? Teacher, IReadOnlyList<SessionDto> Sessions);

/// <summary>
/// Links a teacher profile to a member's login (decision D2: the owner can teach with the same account),
/// and gives that person their own teaching schedule in the center.
/// </summary>
public sealed class TeacherAccountWorkflow(ITeachingStore store, ICenterMemberDirectory members,
    ITeachingNameReader? names = null)
{
    public async Task<Result<TeacherDto>> LinkAsync(Guid centerId, Guid teacherId, Guid? userId,
        int expectedVersion, CancellationToken token)
    {
        if (userId == Guid.Empty || expectedVersion < 0) return Result<TeacherDto>.Failure(TeachingErrors.Invalid);
        await using var guard = await store.BeginGuardAsync(centerId, null, null, teacherId, null, token);
        var teacher = await store.FindTeacherAsync(centerId, teacherId, token);
        if (teacher is null) return Result<TeacherDto>.Failure(TeachingErrors.NotFound);
        if (teacher.Version != expectedVersion) return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!teacher.IsActive) return Result<TeacherDto>.Failure(TeachingErrors.TeacherUnavailable);
        if (userId.HasValue)
        {
            if (await members.FindActiveAsync(centerId, userId.Value, token) is null)
                return Result<TeacherDto>.Failure(TeachingErrors.AccountNotMember);
            var other = await store.FindTeacherByUserAsync(centerId, userId.Value, token);
            if (other is not null && other.Id != teacher.Id) return Result<TeacherDto>.Failure(TeachingErrors.AccountInUse);
        }

        teacher.LinkAccount(userId);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_teachers_center_user")
        { return Result<TeacherDto>.Failure(TeachingErrors.AccountInUse); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<TeacherDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<TeacherDto>.Success(TeacherDto.From(teacher));
    }

    public async Task<Result<MyTeachingDto>> MyTeachingAsync(Guid centerId, Guid userId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero || toUtc <= fromUtc ||
            toUtc - fromUtc > TimeSpan.FromDays(31))
            return Result<MyTeachingDto>.Failure(TeachingErrors.Invalid);
        var teacher = await store.FindTeacherByUserAsync(centerId, userId, token);
        if (teacher is null) return Result<MyTeachingDto>.Success(new MyTeachingDto(null, []));
        var sessions = await store.ListTeacherSessionsAsync(centerId, teacher.Id, fromUtc, toUtc, token);
        var lookup = names is null || sessions.Count == 0 ? TeachingNames.Empty
            : await names.ReadAsync(centerId, sessions.Select(x => x.ClassId), [teacher.Id],
                sessions.Select(x => x.RoomId), token);
        return Result<MyTeachingDto>.Success(new MyTeachingDto(TeacherDto.From(teacher),
            sessions.Select(x => SessionDto.From(x).WithNames(lookup)).ToArray()));
    }
}
