using System.Security.Cryptography;
using System.Text;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

// ---------- DTOs ----------

/// <summary>Returned once, when the link is created: the token is not stored in clear and cannot be read again.</summary>
public sealed record ActivationLinkDto(Guid StudentId, string StudentCode, string FullName, string Token,
    DateTimeOffset ExpiresAtUtc);

/// <summary>What the center sees about a profile's latest activation link.</summary>
/// <remarks>A class with a parameterless constructor: it is part of StudentDto, which also serializes as XML.</remarks>
public sealed class ActivationStatusDto
{
    /// <summary>Active, Used, Declined, Revoked or Expired.</summary>
    public string State { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public DateTimeOffset? DeclinedAtUtc { get; set; }
}

public sealed record ActivationPreviewDto(string CenterName, string StudentCode, string FullName,
    IReadOnlyList<string> ClassNames, bool AlreadyLinkedToYou);

public sealed record ActivationResultDto(Guid CenterId, Guid StudentId, string CenterName, string StudentCode);

public sealed record ClassInviteDto(Guid Id, Guid ClassId, string Token, string Code, DateTimeOffset ExpiresAtUtc,
    int? MaxUses, int UseCount, DateTimeOffset CreatedAtUtc, DateTimeOffset? RevokedAtUtc, bool IsActive);

public sealed record ClassInvitePreviewDto(Guid CenterId, string CenterName, Guid ClassId, string ClassName,
    string SubjectName, IReadOnlyList<string> TeacherNames, IReadOnlyList<WeeklySlotDto> WeeklySchedule,
    DateOnly? StartDate, DateOnly? EndDate, int SeatsLeft, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>The signed-in student already has a profile in this center (no new profile will be made).</summary>
    public bool HasProfileHere { get; init; }
    public bool AlreadyInClass { get; init; }
    public string? ProfileName { get; init; }
    public string? ParentName { get; init; }
    public string? ParentPhone { get; init; }
}

public sealed record ClassInviteJoinResultDto(Guid CenterId, Guid ClassId, Guid StudentId, string StudentCode,
    bool CreatedProfile, int OtherCenterConflictCount);

public sealed record ClassInviteJoinDto(Guid Id, Guid ClassId, Guid StudentId, string StudentCode, string FullName,
    bool CreatedProfile, bool HadConflicts, DateTimeOffset JoinedAtUtc);

public sealed record MergeResultDto(Guid TargetStudentId, Guid SourceStudentId, int MovedEnrollments,
    int MovedAttendance);

// ---------- Store ----------

public sealed record StudentClassName(Guid StudentId, string ClassName);
public sealed record InviteClassRow(Guid Id, Guid CenterId, string Name, string SubjectName, int SeatsLeft, bool IsActive);

public interface IStudentLinkStore
{
    Task<Student?> FindStudentAsync(Guid centerId, Guid studentId, CancellationToken token);
    Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken token);
    /// <summary>Active profiles without a login whose name matches (a hint for the center only).</summary>
    Task<Student?> FindUnlinkedNamesakeAsync(Guid centerId, string fullName, Guid exceptId, CancellationToken token);
    Task<IReadOnlyList<Student>> ListUnlinkedEnrolledAsync(Guid centerId, Guid classId, CancellationToken token);
    Task<IReadOnlyList<StudentClassName>> ListActiveClassNamesAsync(Guid centerId, IReadOnlyCollection<Guid> studentIds,
        CancellationToken token);
    Task<IReadOnlyList<StudentActivationLink>> ListActiveActivationLinksAsync(Guid centerId, Guid studentId,
        CancellationToken token);
    Task<StudentActivationLink?> FindActivationLinkAsync(string tokenHash, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, StudentActivationLink>> LatestActivationLinksAsync(Guid centerId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken token);
    Task<IReadOnlyList<ClassInviteLink>> ListInvitesAsync(Guid centerId, Guid classId, CancellationToken token);
    Task<ClassInviteLink?> FindInviteAsync(Guid centerId, Guid inviteId, CancellationToken token);
    Task<ClassInviteLink?> FindInviteByTokenAsync(string token, CancellationToken token2);
    Task<ClassInviteAttempt?> FindAttemptAsync(Guid linkId, Guid userId, CancellationToken token);
    Task<InviteClassRow?> FindInviteClassAsync(Guid centerId, Guid classId, CancellationToken token);
    Task<IReadOnlyList<ClassInviteJoin>> ListJoinsAsync(Guid centerId, Guid? classId, int take, CancellationToken token);
    Task<bool> CenterApprovedAsync(Guid centerId, CancellationToken token);
    Task<bool> HasActiveEnrollmentAsync(Guid centerId, Guid classId, Guid studentId, CancellationToken token);
    /// <summary>
    /// One transaction holding advisory locks on a login in the center, a class and profiles — the same keys class
    /// registration ("application-user"), enrollment ("class", "student") and scheduling use.
    /// </summary>
    Task<ITeachingGuard> BeginGuardAsync(Guid centerId, Guid? userId, Guid? classId, IReadOnlyCollection<Guid> studentIds,
        CancellationToken token);
    /// <summary>Classes both profiles are actively enrolled in (a merge must not create two seats).</summary>
    Task<IReadOnlyList<string>> SharedActiveClassNamesAsync(Guid centerId, Guid a, Guid b, CancellationToken token);
    Task<bool> SharedAttendanceAsync(Guid centerId, Guid a, Guid b, CancellationToken token);
    /// <summary>Moves enrollments, attendance, requests, notices and join rows from source to target.</summary>
    Task<(int Enrollments, int Attendance)> MoveStudentDataAsync(Guid centerId, Guid sourceId, Guid targetId,
        CancellationToken token);
    Task AddAsync<T>(T entity, CancellationToken token) where T : class;
    Task SaveAsync(CancellationToken token);
}

public static class StudentLinkErrors
{
    public static readonly Error ActivationInvalid = new("activation_link_invalid",
        "This activation link is invalid, expired, revoked or already used. Ask the center for a new one.");
    public static readonly Error ProfileAlreadyLinked = new("student_profile_already_linked",
        "This profile is already linked to a login.");
    public static readonly Error AccountHasProfile = new("activation_account_has_profile",
        "Your login already has another profile at this center. The center has been told to merge the two profiles.");
    public static readonly Error ProfileUnavailable = new("student_profile_unavailable",
        "The profile is archived or merged.");
    public static readonly Error NotLinked = new("student_profile_not_linked", "The profile has no linked login.");
    public static readonly Error InviteInvalid = new("class_invite_invalid",
        "This class link is invalid, expired, revoked or has no uses left. Ask the center for a new one.");
    public static readonly Error InviteLocked = new("class_invite_locked",
        "Too many wrong codes. Try again in 15 minutes.", "code");
    public static readonly Error WrongCode = new("class_invite_wrong_code", "The code is not correct.", "code");
    public static readonly Error UseActivationLink = new("class_invite_use_activation_link",
        "You already study at this center: ask the center for your personal activation link instead of joining here.");
    public static readonly Error InvalidInvite = new("validation_error",
        "validDays must be 1, 7 or 30 and maxUses 1–500 when given.", "validDays");
    public static readonly Error MergeInvalid = new("student_merge_invalid",
        "Choose two different active profiles of this center.");
    public static readonly Error MergeTwoAccounts = new("student_merge_two_accounts",
        "Both profiles are linked to different logins; unlink one first.");
    public static readonly Error MergeSharedClass = new("student_merge_shared_class",
        "Both profiles are enrolled in the same class; withdraw one of them first.");
    public static readonly Error MergeSharedAttendance = new("student_merge_shared_attendance",
        "Both profiles were marked in the same session, so they look like two different students.");
}

// ---------- Center side ----------

/// <summary>
/// M3 for the center: activation links for hand-made profiles, class invite links, unlinking, merging duplicates.
/// </summary>
public sealed class StudentAccessWorkflow(IStudentLinkStore store, ITeachingStore teaching, IClock clock)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<Result<ActivationLinkDto>> CreateActivationLinkAsync(Guid centerId, Guid studentId, Guid actorId,
        CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, null, null, [studentId], token);
        var student = await store.FindStudentAsync(centerId, studentId, token);
        if (student is null) return Result<ActivationLinkDto>.Failure(TeachingErrors.NotFound);
        if (!student.IsActive || student.MergedIntoStudentId.HasValue)
            return Result<ActivationLinkDto>.Failure(StudentLinkErrors.ProfileUnavailable);
        if (student.UserId.HasValue) return Result<ActivationLinkDto>.Failure(StudentLinkErrors.ProfileAlreadyLinked);
        var dto = await IssueAsync(student, actorId, token);
        await teaching.SaveAsync(token);
        await guard.CommitAsync(token);
        return Result<ActivationLinkDto>.Success(dto);
    }

    /// <summary>One link per profile of the class that has no login yet (the "phát cho cả lớp" list).</summary>
    public async Task<Result<IReadOnlyList<ActivationLinkDto>>> CreateClassActivationLinksAsync(Guid centerId,
        Guid classId, Guid actorId, CancellationToken token)
    {
        if (await teaching.FindClassAsync(centerId, classId, token) is null)
            return Result<IReadOnlyList<ActivationLinkDto>>.Failure(TeachingErrors.NotFound);
        var students = await store.ListUnlinkedEnrolledAsync(centerId, classId, token);
        if (students.Count == 0) return Result<IReadOnlyList<ActivationLinkDto>>.Success([]);
        await using var guard = await store.BeginGuardAsync(centerId, null, null, students.Select(x => x.Id).ToArray(), token);
        var items = new List<ActivationLinkDto>();
        foreach (var student in students)
        {
            var fresh = await store.FindStudentAsync(centerId, student.Id, token);
            if (fresh is null || !fresh.IsActive || fresh.UserId.HasValue) continue;
            items.Add(await IssueAsync(fresh, actorId, token));
        }
        await teaching.SaveAsync(token);
        await guard.CommitAsync(token);
        return Result<IReadOnlyList<ActivationLinkDto>>.Success(items.OrderBy(x => x.FullName, StringComparer.CurrentCulture).ToArray());
    }

    public async Task<Result<bool>> RevokeActivationLinksAsync(Guid centerId, Guid studentId, CancellationToken token)
    {
        if (await store.FindStudentAsync(centerId, studentId, token) is null)
            return Result<bool>.Failure(TeachingErrors.NotFound);
        var now = clock.UtcNow;
        foreach (var link in await store.ListActiveActivationLinksAsync(centerId, studentId, token)) link.Revoke(now);
        await teaching.SaveAsync(token);
        return Result<bool>.Success(true);
    }

    /// <summary>"Gỡ tài khoản": the login keeps working but no longer sees this profile.</summary>
    public async Task<Result<StudentDto>> UnlinkAccountAsync(Guid centerId, Guid studentId, int expectedVersion,
        CancellationToken token)
    {
        await using var guard = await store.BeginGuardAsync(centerId, null, null, [studentId], token);
        var student = await store.FindStudentAsync(centerId, studentId, token);
        if (student is null) return Result<StudentDto>.Failure(TeachingErrors.NotFound);
        if (student.Version != expectedVersion) return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!student.UserId.HasValue) return Result<StudentDto>.Failure(StudentLinkErrors.NotLinked);
        student.UnlinkAccount();
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<StudentDto>.Success(StudentDto.From(student));
    }

    public async Task<Result<StudentDto>> DismissDuplicateAsync(Guid centerId, Guid studentId, int expectedVersion,
        CancellationToken token)
    {
        var student = await store.FindStudentAsync(centerId, studentId, token);
        if (student is null) return Result<StudentDto>.Failure(TeachingErrors.NotFound);
        if (student.Version != expectedVersion) return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange);
        student.ClearDuplicate();
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<StudentDto>.Failure(TeachingErrors.ConcurrentChange); }
        return Result<StudentDto>.Success(StudentDto.From(student));
    }

    /// <summary>
    /// "Gộp sourceId vào targetId": enrollments, attendance, requests and notices move to the target, the login (if
    /// only the source has one) moves too, and the source is archived with a pointer to the target. One transaction.
    /// </summary>
    public async Task<Result<MergeResultDto>> MergeAsync(Guid centerId, Guid targetId, Guid sourceId,
        CancellationToken token)
    {
        if (targetId == sourceId || targetId == Guid.Empty || sourceId == Guid.Empty)
            return Result<MergeResultDto>.Failure(StudentLinkErrors.MergeInvalid);
        await using var guard = await store.BeginGuardAsync(centerId, null, null, [targetId, sourceId], token);
        var target = await store.FindStudentAsync(centerId, targetId, token);
        var source = await store.FindStudentAsync(centerId, sourceId, token);
        if (target is null || source is null) return Result<MergeResultDto>.Failure(TeachingErrors.NotFound);
        if (target.MergedIntoStudentId.HasValue || source.MergedIntoStudentId.HasValue || !target.IsActive)
            return Result<MergeResultDto>.Failure(StudentLinkErrors.MergeInvalid);
        if (target.UserId.HasValue && source.UserId.HasValue && target.UserId != source.UserId)
            return Result<MergeResultDto>.Failure(StudentLinkErrors.MergeTwoAccounts);
        if ((await store.SharedActiveClassNamesAsync(centerId, targetId, sourceId, token)).Count > 0)
            return Result<MergeResultDto>.Failure(StudentLinkErrors.MergeSharedClass);
        if (await store.SharedAttendanceAsync(centerId, targetId, sourceId, token))
            return Result<MergeResultDto>.Failure(StudentLinkErrors.MergeSharedAttendance);

        var (enrollments, attendance) = await store.MoveStudentDataAsync(centerId, sourceId, targetId, token);
        var login = source.UserId;
        target.AdoptParentContactIfMissing(source.ParentName, source.ParentPhone);
        if (target.DuplicateCandidateId == sourceId) target.ClearDuplicate();
        source.MarkMergedInto(targetId, clock.UtcNow);
        foreach (var link in await store.ListActiveActivationLinksAsync(centerId, sourceId, token)) link.Revoke(clock.UtcNow);
        try
        {
            // The source must drop the login before the target takes it (one login per profile per center).
            await teaching.SaveAsync(token);
            if (login is { } userId && !target.UserId.HasValue)
            {
                target.LinkAccount(userId);
                foreach (var link in await store.ListActiveActivationLinksAsync(centerId, targetId, token))
                    link.Revoke(clock.UtcNow);
                await teaching.SaveAsync(token);
            }
        }
        catch (TeachingPersistenceException) { return Result<MergeResultDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<MergeResultDto>.Success(new MergeResultDto(targetId, sourceId, enrollments, attendance));
    }

    // ----- class invite links -----

    public async Task<Result<IReadOnlyList<ClassInviteDto>>> ListInvitesAsync(Guid centerId, Guid classId,
        CancellationToken token)
    {
        if (await teaching.FindClassAsync(centerId, classId, token) is null)
            return Result<IReadOnlyList<ClassInviteDto>>.Failure(TeachingErrors.NotFound);
        var now = clock.UtcNow;
        return Result<IReadOnlyList<ClassInviteDto>>.Success(
            (await store.ListInvitesAsync(centerId, classId, token)).Select(x => ToDto(x, now)).ToArray());
    }

    public async Task<Result<ClassInviteDto>> CreateInviteAsync(Guid centerId, Guid classId, int validDays,
        int? maxUses, Guid actorId, CancellationToken token)
    {
        if (!ClassInviteLink.AllowedValidDays.Contains(validDays) || maxUses is < 1 or > 500)
            return Result<ClassInviteDto>.Failure(StudentLinkErrors.InvalidInvite);
        var teachingClass = await teaching.FindClassAsync(centerId, classId, token);
        if (teachingClass is null) return Result<ClassInviteDto>.Failure(TeachingErrors.NotFound);
        if (!teachingClass.IsActive) return Result<ClassInviteDto>.Failure(TeachingErrors.ClassUnavailable);
        var now = clock.UtcNow;
        var link = ClassInviteLink.Create(centerId, classId, NewToken(), NewCode(), validDays, maxUses, actorId, now);
        await store.AddAsync(link, token);
        await teaching.SaveAsync(token);
        return Result<ClassInviteDto>.Success(ToDto(link, now));
    }

    public async Task<Result<ClassInviteDto>> RevokeInviteAsync(Guid centerId, Guid inviteId, CancellationToken token)
    {
        var link = await store.FindInviteAsync(centerId, inviteId, token);
        if (link is null) return Result<ClassInviteDto>.Failure(TeachingErrors.NotFound);
        link.Revoke(clock.UtcNow);
        await teaching.SaveAsync(token);
        return Result<ClassInviteDto>.Success(ToDto(link, clock.UtcNow));
    }

    /// <summary>"Đổi mã": the link stays the same, the old code stops working at once.</summary>
    public async Task<Result<ClassInviteDto>> RecodeInviteAsync(Guid centerId, Guid inviteId, CancellationToken token)
    {
        var link = await store.FindInviteAsync(centerId, inviteId, token);
        if (link is null) return Result<ClassInviteDto>.Failure(TeachingErrors.NotFound);
        if (!link.IsActiveAt(clock.UtcNow)) return Result<ClassInviteDto>.Failure(StudentLinkErrors.InviteInvalid);
        link.Recode(NewCode());
        await teaching.SaveAsync(token);
        return Result<ClassInviteDto>.Success(ToDto(link, clock.UtcNow));
    }

    public async Task<IReadOnlyList<ClassInviteJoinDto>> ListJoinsAsync(Guid centerId, Guid? classId,
        CancellationToken token)
    {
        var joins = await store.ListJoinsAsync(centerId, classId, 200, token);
        var students = new Dictionary<Guid, Student?>();
        foreach (var id in joins.Select(x => x.StudentId).Distinct())
            students[id] = await store.FindStudentAsync(centerId, id, token);
        return joins.Select(x => new ClassInviteJoinDto(x.Id, x.ClassId, x.StudentId,
            students[x.StudentId]?.Code ?? string.Empty, students[x.StudentId]?.FullName ?? string.Empty,
            x.CreatedProfile, x.HadConflicts, x.JoinedAtUtc)).ToArray();
    }

    public Task<IReadOnlyDictionary<Guid, StudentActivationLink>> LatestActivationLinksAsync(Guid centerId,
        IReadOnlyCollection<Guid> studentIds, CancellationToken token) =>
        store.LatestActivationLinksAsync(centerId, studentIds, token);

    public static ActivationStatusDto? Status(StudentActivationLink? link, DateTimeOffset now) => link is null ? null
        : new ActivationStatusDto
        {
            State = link.UsedAtUtc.HasValue ? "Used" : link.DeclinedAtUtc.HasValue ? "Declined" :
                link.RevokedAtUtc.HasValue ? "Revoked" : link.ExpiresAtUtc <= now ? "Expired" : "Active",
            CreatedAtUtc = link.CreatedAtUtc, ExpiresAtUtc = link.ExpiresAtUtc, UsedAtUtc = link.UsedAtUtc,
            DeclinedAtUtc = link.DeclinedAtUtc
        };

    private async Task<ActivationLinkDto> IssueAsync(Student student, Guid actorId, CancellationToken token)
    {
        var now = clock.UtcNow;
        foreach (var old in await store.ListActiveActivationLinksAsync(student.CenterId, student.Id, token)) old.Revoke(now);
        var raw = NewToken();
        var link = StudentActivationLink.Create(student.CenterId, student.Id, Hash(raw), actorId, now);
        await store.AddAsync(link, token);
        return new ActivationLinkDto(student.Id, student.Code, student.FullName, raw, link.ExpiresAtUtc);
    }

    private static ClassInviteDto ToDto(ClassInviteLink x, DateTimeOffset now) => new(x.Id, x.ClassId, x.Token, x.Code,
        x.ExpiresAtUtc, x.MaxUses, x.UseCount, x.CreatedAtUtc, x.RevokedAtUtc, x.IsActiveAt(now));

    internal static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static string NewCode() => string.Create(ClassInviteLink.CodeLength, 0, (span, _) =>
    {
        for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
    });

    internal static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

// ---------- Student side ----------

/// <summary>M3 for the student: open an activation link or a class invite link while signed in with a student login.</summary>
public sealed class StudentJoinWorkflow(IStudentLinkStore store, ITeachingStore teaching, IStudentTimetableStore timetable,
    ICourseDirectoryStore directory, IClock clock, ScheduleConflictChecker? conflicts = null)
{
    public async Task<Result<ActivationPreviewDto>> PreviewActivationAsync(string token, Guid userId,
        CancellationToken cancellationToken)
    {
        var (link, student) = await LoadActivationAsync(token, cancellationToken);
        if (student is not null && student.UserId == userId && link is not null)
            return Result<ActivationPreviewDto>.Success(await PreviewAsync(student, true, cancellationToken));
        if (link is null || student is null || !link.IsActiveAt(clock.UtcNow) || !student.IsActive ||
            student.MergedIntoStudentId.HasValue)
            return Result<ActivationPreviewDto>.Failure(StudentLinkErrors.ActivationInvalid);
        if (student.UserId.HasValue) return Result<ActivationPreviewDto>.Failure(StudentLinkErrors.ProfileAlreadyLinked);
        return Result<ActivationPreviewDto>.Success(await PreviewAsync(student, false, cancellationToken));
    }

    /// <summary>
    /// "Đúng là tôi": the login is attached to the link's profile. If the login already has another profile in this
    /// center, nothing is attached; both profiles are flagged for the center to merge (H3 variant).
    /// </summary>
    public async Task<Result<ActivationResultDto>> AcceptActivationAsync(string token, Guid userId,
        CancellationToken cancellationToken)
    {
        var (preliminary, _) = await LoadActivationAsync(token, cancellationToken);
        if (preliminary is null) return Result<ActivationResultDto>.Failure(StudentLinkErrors.ActivationInvalid);
        await using var guard = await store.BeginGuardAsync(preliminary.CenterId, userId, null, [preliminary.StudentId],
            cancellationToken);
        var (link, student) = await LoadActivationAsync(token, cancellationToken);
        if (link is null || student is null) return Result<ActivationResultDto>.Failure(StudentLinkErrors.ActivationInvalid);
        var centerName = (await timetable.ReadCenterNamesAsync([student.CenterId], cancellationToken))
            .GetValueOrDefault(student.CenterId) ?? string.Empty;
        if (student.UserId == userId)
            return Result<ActivationResultDto>.Success(new(student.CenterId, student.Id, centerName, student.Code));
        var now = clock.UtcNow;
        if (!link.IsActiveAt(now) || !student.IsActive || student.MergedIntoStudentId.HasValue)
            return Result<ActivationResultDto>.Failure(StudentLinkErrors.ActivationInvalid);
        if (student.UserId.HasValue) return Result<ActivationResultDto>.Failure(StudentLinkErrors.ProfileAlreadyLinked);

        var existing = await store.FindStudentForUserAsync(student.CenterId, userId, cancellationToken);
        if (existing is not null)
        {
            student.FlagDuplicate(existing.Id, StudentDuplicateReason.SameAccount);
            existing.FlagDuplicate(student.Id, StudentDuplicateReason.SameAccount);
            await teaching.SaveAsync(cancellationToken);
            await guard.CommitAsync(cancellationToken);
            return Result<ActivationResultDto>.Failure(StudentLinkErrors.AccountHasProfile);
        }

        student.LinkAccount(userId);
        link.MarkUsed(userId, now);
        try { await teaching.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException) { return Result<ActivationResultDto>.Failure(StudentLinkErrors.AccountHasProfile); }
        await guard.CommitAsync(cancellationToken);
        return Result<ActivationResultDto>.Success(new(student.CenterId, student.Id, centerName, student.Code));
    }

    /// <summary>"Không phải tôi": the link stops working and the center sees that it reached the wrong person.</summary>
    public async Task<Result<bool>> DeclineActivationAsync(string token, CancellationToken cancellationToken)
    {
        var (link, _) = await LoadActivationAsync(token, cancellationToken);
        if (link is null || !link.IsActiveAt(clock.UtcNow)) return Result<bool>.Failure(StudentLinkErrors.ActivationInvalid);
        link.Decline(clock.UtcNow);
        await teaching.SaveAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ClassInvitePreviewDto>> PreviewInviteAsync(string token, Guid userId,
        CancellationToken cancellationToken)
    {
        var link = await store.FindInviteByTokenAsync(token, cancellationToken);
        var now = clock.UtcNow;
        if (link is null || !link.IsActiveAt(now) || !await store.CenterApprovedAsync(link.CenterId, cancellationToken))
            return Result<ClassInvitePreviewDto>.Failure(StudentLinkErrors.InviteInvalid);
        var row = await store.FindInviteClassAsync(link.CenterId, link.ClassId, cancellationToken);
        if (row is null || !row.IsActive) return Result<ClassInvitePreviewDto>.Failure(StudentLinkErrors.InviteInvalid);
        var centerName = (await timetable.ReadCenterNamesAsync([link.CenterId], cancellationToken))
            .GetValueOrDefault(link.CenterId) ?? string.Empty;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, CenterTime.Zone).Date);
        var series = await directory.ListRunningSeriesAsync(link.CenterId, [link.ClassId], today, cancellationToken);
        var spans = await directory.ListSessionSpansAsync(link.CenterId, [link.ClassId], now, cancellationToken);
        var span = spans.Count > 0 ? spans[0] : null;
        var slots = series.SelectMany(x => Enumerable.Range(0, 7).Where(d => (x.WeekdaysMask & (1 << d)) != 0)
                .Select(d => new WeeklySlotDto(((DayOfWeek)d).ToString(), x.LocalStartTime, x.DurationMinutes)))
            .Distinct().OrderBy(x => Order(x.Weekday)).ThenBy(x => x.StartTime).ToArray();
        var teachers = series.Select(x => x.TeacherName).Concat(span?.TeacherNames ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
        var profile = await store.FindStudentForUserAsync(link.CenterId, userId, cancellationToken);
        return Result<ClassInvitePreviewDto>.Success(new ClassInvitePreviewDto(link.CenterId, centerName, link.ClassId,
            row.Name, row.SubjectName, teachers, slots, span is null ? null : LocalDate(span.FirstStartUtc),
            span is null ? null : LocalDate(span.LastEndUtc), Math.Max(0, row.SeatsLeft), link.ExpiresAtUtc)
        {
            HasProfileHere = profile is not null,
            AlreadyInClass = profile is not null &&
                await store.HasActiveEnrollmentAsync(link.CenterId, link.ClassId, profile.Id, cancellationToken),
            ProfileName = profile?.FullName, ParentName = profile?.ParentName, ParentPhone = profile?.ParentPhone
        });
    }

    /// <summary>
    /// Joins the class with the right code, pre-approved: reuses the login's profile in this center or creates one.
    /// A student who says they studied here before but has no linked profile is sent to their activation link.
    /// </summary>
    public async Task<(Result<ClassInviteJoinResultDto> Result, ScheduleCheckDto? Conflicts)> JoinAsync(string token,
        Guid userId, string? code, string? fullName, string? parentName, string? parentPhone, bool studiedHereBefore,
        bool acknowledgeConflicts, CancellationToken cancellationToken)
    {
        var preliminary = await store.FindInviteByTokenAsync(token, cancellationToken);
        if (preliminary is null) return (Result<ClassInviteJoinResultDto>.Failure(StudentLinkErrors.InviteInvalid), null);
        var centerId = preliminary.CenterId;
        var classId = preliminary.ClassId;
        await using var guard = await store.BeginGuardAsync(centerId, userId, classId, [], cancellationToken);
        var link = await store.FindInviteByTokenAsync(token, cancellationToken);
        var now = clock.UtcNow;
        if (link is null || !link.IsActiveAt(now) || !await store.CenterApprovedAsync(centerId, cancellationToken))
            return (Result<ClassInviteJoinResultDto>.Failure(StudentLinkErrors.InviteInvalid), null);

        var attempt = await store.FindAttemptAsync(link.Id, userId, cancellationToken);
        if (attempt?.IsLocked(now) == true)
            return (Result<ClassInviteJoinResultDto>.Failure(StudentLinkErrors.InviteLocked), null);
        if (!link.Matches(code))
        {
            if (attempt is null)
            {
                attempt = new ClassInviteAttempt(link.Id, userId);
                await store.AddAsync(attempt, cancellationToken);
            }
            attempt.RecordFailure(now);
            await teaching.SaveAsync(cancellationToken);
            await guard.CommitAsync(cancellationToken);
            return (Result<ClassInviteJoinResultDto>.Failure(attempt.IsLocked(now)
                ? StudentLinkErrors.InviteLocked : StudentLinkErrors.WrongCode), null);
        }
        attempt?.Reset();

        var teachingClass = await teaching.FindClassAsync(centerId, classId, cancellationToken);
        if (teachingClass is null || !teachingClass.IsActive)
            return (Result<ClassInviteJoinResultDto>.Failure(StudentLinkErrors.InviteInvalid), null);
        var student = await store.FindStudentForUserAsync(centerId, userId, cancellationToken);
        if (student is null && studiedHereBefore)
            return (Result<ClassInviteJoinResultDto>.Failure(StudentLinkErrors.UseActivationLink), null);
        if (student is { IsActive: false })
            return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.StudentArchived), null);
        if (student is not null && await store.HasActiveEnrollmentAsync(centerId, classId, student.Id, cancellationToken))
            return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.AlreadyEnrolled), null);
        if (teachingClass.EnrolledCount >= teachingClass.MaxStudents)
            return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.Full), null);

        string? normalizedPhone = null;
        if (student is null)
        {
            if (!TeachingClass.IsValidName(fullName ?? string.Empty))
                return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.InvalidName), null);
            if (!ParentContact.IsValidName(parentName))
                return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.InvalidParentName), null);
            if (!ParentContact.TryNormalizePhone(parentPhone, out normalizedPhone))
                return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.InvalidParentPhone), null);
        }

        var check = conflicts is null ? ScheduleCheckDto.Clear
            : await conflicts.CheckJoinAsync(centerId, classId, student?.Id ?? Guid.Empty, userId, cancellationToken);
        if (check.HasSameCenterConflicts && !acknowledgeConflicts)
            return (Result<ClassInviteJoinResultDto>.Failure(TeachingErrors.StudentScheduleConflict), check);

        var created = student is null;
        if (student is null)
        {
            var studentCode = await teaching.NextStudentCodeAsync(centerId, cancellationToken);
            student = Student.CreateForUser(centerId, userId, studentCode, fullName!, now);
            student.AdoptParentContactIfMissing(parentName, normalizedPhone);
            await store.AddAsync(student, cancellationToken);
            var namesake = await store.FindUnlinkedNamesakeAsync(centerId, student.FullName, student.Id, cancellationToken);
            if (namesake is not null) student.FlagDuplicate(namesake.Id, StudentDuplicateReason.SameName);
        }
        else if (parentName is not null && parentPhone is not null)
        {
            student.AdoptParentContactIfMissing(parentName, parentPhone);
        }

        teachingClass.Enroll(now);
        link.RecordUse();
        await store.AddAsync(Enrollment.Create(centerId, classId, student.Id, now), cancellationToken);
        await store.AddAsync(new ClassInviteJoin(centerId, link.Id, classId, student.Id, userId, created,
            check.HasSameCenterConflicts, now), cancellationToken);
        try { await teaching.SaveAsync(cancellationToken); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_enrollments_active_class_student")
        { return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.AlreadyEnrolled), null); }
        catch (TeachingPersistenceException)
        { return (Result<ClassInviteJoinResultDto>.Failure(ClassRegistrationErrors.ConcurrentChange), null); }
        await guard.CommitAsync(cancellationToken);
        return (Result<ClassInviteJoinResultDto>.Success(new ClassInviteJoinResultDto(centerId, classId, student.Id,
            student.Code, created, check.OtherCenterConflictCount)), null);
    }

    private async Task<(StudentActivationLink? Link, Student? Student)> LoadActivationAsync(string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return (null, null);
        var link = await store.FindActivationLinkAsync(StudentAccessWorkflow.Hash(token.Trim()), cancellationToken);
        if (link is null) return (null, null);
        return (link, await store.FindStudentAsync(link.CenterId, link.StudentId, cancellationToken));
    }

    private async Task<ActivationPreviewDto> PreviewAsync(Student student, bool mine, CancellationToken token)
    {
        var center = (await timetable.ReadCenterNamesAsync([student.CenterId], token)).GetValueOrDefault(student.CenterId)
            ?? string.Empty;
        var classes = await store.ListActiveClassNamesAsync(student.CenterId, [student.Id], token);
        return new ActivationPreviewDto(center, student.Code, student.FullName,
            classes.Select(x => x.ClassName).ToArray(), mine);
    }

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, CenterTime.Zone).Date);

    private static int Order(string weekday) =>
        Enum.TryParse<DayOfWeek>(weekday, out var day) ? (day == DayOfWeek.Sunday ? 7 : (int)day) : 8;
}
