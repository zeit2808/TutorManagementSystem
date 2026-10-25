using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public interface IStudentRequestStore
{
    Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken token);
    Task<StudentRequest?> FindAsync(Guid centerId, Guid id, CancellationToken token);
    Task<StudentRequest?> FindSnapshotAsync(Guid centerId, Guid id, CancellationToken token);
    /// <summary>A pending request of the kind for the same session (absence, makeup) or class (transfer, withdrawal),
    /// or an approved absence for the same session.</summary>
    Task<bool> HasOpenAsync(Guid studentId, StudentRequestKind kind, Guid classId, Guid? sessionId, CancellationToken token);
    Task<ScheduleNotice?> FindNoticeAsync(Guid centerId, Guid noticeId, Guid studentId, CancellationToken token);
    Task<(IReadOnlyList<StudentRequest> Items, int TotalCount)> ListCenterAsync(Guid centerId,
        StudentRequestStatus? status, StudentRequestKind? kind, Guid? classId, int page, int pageSize, CancellationToken token);
    Task<(IReadOnlyList<StudentRequest> Items, int TotalCount)> ListMineAsync(Guid userId, int page, int pageSize,
        CancellationToken token);
    Task<IReadOnlyDictionary<Guid, (string Code, string Name)>> ReadStudentsAsync(IReadOnlyCollection<Guid> studentIds,
        CancellationToken token);
    Task<IReadOnlyDictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)>> ReadSessionTimesAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken token);
    Task<bool> SheetSubmittedAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task AddAsync(StudentRequest request, CancellationToken token);
}

public sealed record StudentRequestDto(Guid Id, Guid CenterId, string Kind, string Status, Guid ClassId,
    Guid? SessionId, Guid? TargetClassId, Guid StudentId, string Reason, DateTimeOffset SubmittedAtUtc,
    DateTimeOffset? ReviewedAtUtc, string? ReviewNote, int Version)
{
    public string? CenterName { get; init; }
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? TargetClassCode { get; init; }
    public string? TargetClassName { get; init; }
    public DateTimeOffset? SessionStartUtc { get; init; }
    public DateTimeOffset? SessionEndUtc { get; init; }
    public string? StudentCode { get; init; }
    public string? StudentName { get; init; }
    public string? ReviewedByName { get; init; }
    /// <summary>Approving a transfer: sessions of the new class that clash with another center (count only).</summary>
    public int? OtherCenterConflictCount { get; init; }

    public static StudentRequestDto From(StudentRequest x) => new(x.Id, x.CenterId, x.Kind.ToString(),
        x.Status.ToString(), x.ClassId, x.SessionId, x.TargetClassId, x.StudentId, x.Reason, x.SubmittedAtUtc,
        x.ReviewedAtUtc, x.ReviewNote, x.Version);
}

public sealed record SubmitStudentRequestInput(string Kind, Guid ClassId, Guid? SessionId, Guid? TargetClassId,
    Guid? NoticeId, string Reason);

public static class StudentRequestErrors
{
    public static readonly Error Invalid = new("invalid_student_request",
        "Kind must be ExcusedAbsence, Makeup, Transfer or Withdrawal; absence and makeup need a session, transfer a target class.");
    public static readonly Error InvalidReason = new("invalid_request_reason", "A reason of 1–500 characters is required.", "reason");
    public static readonly Error NoStudentProfile = new("student_profile_not_found",
        "Your account has no student profile in this center.");
    public static readonly Error NotEnrolled = new("request_class_not_enrolled", "You are not enrolled in this class.", "classId");
    public static readonly Error InvalidSession = new("request_session_invalid",
        "The session must belong to your class and, for an absence, must not have started.", "sessionId");
    public static readonly Error InvalidTarget = new("request_target_invalid",
        "The target class must be another active class of this center that you are not enrolled in.", "targetClassId");
    public static readonly Error Duplicate = new("student_request_exists", "You already sent this request.");
    public static readonly Error NotFound = new("student_request_not_found", "The request was not found.");
    public static readonly Error NotPending = new("student_request_not_pending", "This request has already been handled.");
    public static readonly Error TooLate = new("student_request_too_late",
        "The session was cancelled or its attendance is already submitted; reject the request instead.");
    public static readonly Error ConcurrentChange = new("student_request_concurrent_change", "The request changed. Reload and retry.");
}

/// <summary>
/// B8: requests students send after a schedule change (or any time): excused absence, makeup, transfer and
/// withdrawal. Owner/manager review them; approving a transfer moves the enrollment in one transaction and
/// approving a withdrawal ends it now (D7).
/// </summary>
public sealed class StudentRequestWorkflow(IStudentRequestStore requests, ITeachingStore teaching,
    IStudentTimetableStore timetable, ScheduleConflictChecker conflicts, IClock clock, IUserDirectory? users = null)
{
    public async Task<Result<StudentRequestDto>> SubmitAsync(Guid centerId, Guid userId, SubmitStudentRequestInput input,
        CancellationToken token)
    {
        if (input is null || !Enum.TryParse<StudentRequestKind>(input.Kind, true, out var kind) || !Enum.IsDefined(kind) ||
            int.TryParse(input.Kind, out _) || input.ClassId == Guid.Empty)
            return Result<StudentRequestDto>.Failure(StudentRequestErrors.Invalid);
        var needsSession = kind is StudentRequestKind.ExcusedAbsence or StudentRequestKind.Makeup;
        var hasSession = input.SessionId.HasValue && input.SessionId.Value != Guid.Empty;
        var hasTarget = input.TargetClassId.HasValue && input.TargetClassId.Value != Guid.Empty;
        if (needsSession != hasSession || (kind == StudentRequestKind.Transfer) != hasTarget)
            return Result<StudentRequestDto>.Failure(StudentRequestErrors.Invalid);
        if (!StudentRequest.IsValidReason(input.Reason)) return Result<StudentRequestDto>.Failure(StudentRequestErrors.InvalidReason);

        var student = await requests.FindStudentForUserAsync(centerId, userId, token);
        if (student is null || !student.IsActive) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NoStudentProfile);
        await using var guard = await teaching.BeginGuardAsync(centerId, null, student.Id, null, null, token);
        var enrollment = await teaching.FindActiveEnrollmentAsync(centerId, input.ClassId, student.Id, token);
        if (enrollment is null) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NotEnrolled);
        if (needsSession)
        {
            var session = await teaching.FindSessionSnapshotAsync(centerId, input.SessionId!.Value, token);
            var now = clock.UtcNow;
            if (session is null || session.ClassId != input.ClassId || session.StartUtc < enrollment.EnrolledAtUtc ||
                (kind == StudentRequestKind.ExcusedAbsence && (!session.IsActive || session.StartUtc <= now)))
                return Result<StudentRequestDto>.Failure(StudentRequestErrors.InvalidSession);
        }
        if (kind == StudentRequestKind.Transfer)
        {
            var target = await teaching.FindClassAsync(centerId, input.TargetClassId!.Value, token);
            if (target is null || !target.IsActive || target.Id == input.ClassId ||
                await teaching.FindActiveEnrollmentAsync(centerId, target.Id, student.Id, token) is not null)
                return Result<StudentRequestDto>.Failure(StudentRequestErrors.InvalidTarget);
        }
        if (await requests.HasOpenAsync(student.Id, kind, input.ClassId, input.SessionId, token))
            return Result<StudentRequestDto>.Failure(StudentRequestErrors.Duplicate);
        ScheduleNotice? notice = null;
        if (input.NoticeId is { } noticeId && noticeId != Guid.Empty)
        {
            notice = await requests.FindNoticeAsync(centerId, noticeId, student.Id, token);
            if (notice is null) return Result<StudentRequestDto>.Failure(ScheduleChangeErrors.NoticeNotFound);
        }

        var request = StudentRequest.Create(centerId, student.Id, userId, input.ClassId, kind,
            needsSession ? input.SessionId : null, kind == StudentRequestKind.Transfer ? input.TargetClassId : null,
            notice?.Id, input.Reason, clock.UtcNow);
        notice?.RecordRequest(clock.UtcNow);
        await requests.AddAsync(request, token);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName.StartsWith("ux_student_requests_pending", StringComparison.Ordinal))
        { return Result<StudentRequestDto>.Failure(StudentRequestErrors.Duplicate); }
        catch (TeachingPersistenceException) { return Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<StudentRequestDto>.Success((await DecorateAsync([request], token))[0]);
    }

    public async Task<Result<StudentRequestDto>> CancelAsync(Guid centerId, Guid userId, Guid requestId, CancellationToken token)
    {
        var request = await requests.FindAsync(centerId, requestId, token);
        if (request is null || request.UserId != userId) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NotFound);
        if (request.Status != StudentRequestStatus.Pending) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NotPending);
        request.Cancel(clock.UtcNow);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange); }
        return Result<StudentRequestDto>.Success((await DecorateAsync([request], token))[0]);
    }

    public async Task<TeachingPage<StudentRequestDto>> MineAsync(Guid userId, int page, int pageSize, CancellationToken token)
    {
        var (items, total) = await requests.ListMineAsync(userId, page, pageSize, token);
        return new() { Page = page, PageSize = pageSize, TotalCount = total, Items = (await DecorateAsync(items, token)).ToList() };
    }

    public async Task<TeachingPage<StudentRequestDto>> ListAsync(Guid centerId, StudentRequestStatus? status,
        StudentRequestKind? kind, Guid? classId, int page, int pageSize, CancellationToken token)
    {
        var (items, total) = await requests.ListCenterAsync(centerId, status, kind, classId, page, pageSize, token);
        return new() { Page = page, PageSize = pageSize, TotalCount = total, Items = (await DecorateAsync(items, token)).ToList() };
    }

    public async Task<Result<StudentRequestDto>> RejectAsync(Guid centerId, Guid requestId, Guid reviewerId, string reason,
        int expectedVersion, CancellationToken token)
    {
        if (!StudentRequest.IsValidReason(reason)) return Result<StudentRequestDto>.Failure(StudentRequestErrors.InvalidReason);
        var request = await requests.FindAsync(centerId, requestId, token);
        if (request is null) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NotFound);
        if (request.Version != expectedVersion) return Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange);
        if (request.Status != StudentRequestStatus.Pending) return Result<StudentRequestDto>.Failure(StudentRequestErrors.NotPending);
        request.Reject(reviewerId, reason, clock.UtcNow);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange); }
        return Result<StudentRequestDto>.Success((await DecorateAsync([request], token))[0]);
    }

    /// <summary>
    /// Approves a request. A transfer into a class whose sessions clash with the student's other classes in this
    /// center is refused (the check is returned) unless <paramref name="acknowledgeConflicts"/> is set.
    /// </summary>
    public async Task<(Result<StudentRequestDto> Result, ScheduleCheckDto? Conflicts)> ApproveAsync(Guid centerId,
        Guid requestId, Guid reviewerId, string? note, int expectedVersion, bool acknowledgeConflicts, CancellationToken token)
    {
        if (note is not null && note.Trim().Length > StudentRequest.MaxReasonLength)
            return (Result<StudentRequestDto>.Failure(StudentRequestErrors.InvalidReason), null);
        var snapshot = await requests.FindSnapshotAsync(centerId, requestId, token);
        if (snapshot is null) return (Result<StudentRequestDto>.Failure(StudentRequestErrors.NotFound), null);
        var classIds = new[] { snapshot.ClassId, snapshot.TargetClassId }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        await using var guard = await teaching.BeginEnrollmentGuardAsync(centerId, classIds, snapshot.StudentId, token);
        var request = await requests.FindAsync(centerId, requestId, token);
        if (request is null) return (Result<StudentRequestDto>.Failure(StudentRequestErrors.NotFound), null);
        if (request.Version != expectedVersion) return (Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange), null);
        if (request.Status != StudentRequestStatus.Pending) return (Result<StudentRequestDto>.Failure(StudentRequestErrors.NotPending), null);
        var now = clock.UtcNow;
        int? otherCenter = null;
        switch (request.Kind)
        {
            case StudentRequestKind.ExcusedAbsence:
            {
                var session = await teaching.FindSessionSnapshotAsync(centerId, request.SessionId!.Value, token);
                if (session is null || !session.IsActive || await requests.SheetSubmittedAsync(centerId, session.Id, token))
                    return (Result<StudentRequestDto>.Failure(StudentRequestErrors.TooLate), null);
                break;
            }
            case StudentRequestKind.Makeup:
                break;
            case StudentRequestKind.Withdrawal:
            {
                var current = await teaching.FindActiveEnrollmentAsync(centerId, request.ClassId, request.StudentId, token);
                var teachingClass = await teaching.FindClassAsync(centerId, request.ClassId, token);
                if (current is null || teachingClass is null)
                    return (Result<StudentRequestDto>.Failure(TeachingErrors.NoActiveEnrollment), null);
                current.Withdraw(now, Truncate($"Học sinh xin rút: {request.Reason}"), reviewerId);
                teachingClass.Withdraw(now);
                break;
            }
            case StudentRequestKind.Transfer:
            {
                var current = await teaching.FindActiveEnrollmentAsync(centerId, request.ClassId, request.StudentId, token);
                var from = await teaching.FindClassAsync(centerId, request.ClassId, token);
                var to = await teaching.FindClassAsync(centerId, request.TargetClassId!.Value, token);
                if (current is null || from is null) return (Result<StudentRequestDto>.Failure(TeachingErrors.NoActiveEnrollment), null);
                if (to is null || !to.IsActive) return (Result<StudentRequestDto>.Failure(TeachingErrors.ClassUnavailable), null);
                if (to.EnrolledCount >= to.MaxStudents) return (Result<StudentRequestDto>.Failure(TeachingErrors.Full), null);
                if (await teaching.FindActiveEnrollmentAsync(centerId, to.Id, request.StudentId, token) is not null)
                    return (Result<StudentRequestDto>.Failure(TeachingErrors.AlreadyEnrolled), null);
                // The class being left no longer counts as busy.
                var check = await conflicts.CheckJoinAsync(centerId, to.Id, request.StudentId, request.UserId, token,
                    ignoreClassId: from.Id);
                if (check.HasSameCenterConflicts && !acknowledgeConflicts)
                    return (Result<StudentRequestDto>.Failure(TeachingErrors.StudentScheduleConflict), check);
                otherCenter = check.OtherCenterConflictCount;
                current.Withdraw(now, Truncate($"Chuyển sang lớp {to.Name}: {request.Reason}"), reviewerId);
                from.Withdraw(now);
                to.Enroll(now);
                await teaching.AddAsync(Enrollment.Create(centerId, to.Id, request.StudentId, now), token);
                break;
            }
        }
        request.Approve(reviewerId, note, now);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_enrollments_active_class_student")
        { return (Result<StudentRequestDto>.Failure(TeachingErrors.AlreadyEnrolled), null); }
        catch (TeachingPersistenceException) { return (Result<StudentRequestDto>.Failure(StudentRequestErrors.ConcurrentChange), null); }
        await guard.CommitAsync(token);
        var dto = (await DecorateAsync([request], token))[0] with { OtherCenterConflictCount = otherCenter };
        return (Result<StudentRequestDto>.Success(dto), null);
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];

    private async Task<IReadOnlyList<StudentRequestDto>> DecorateAsync(IReadOnlyList<StudentRequest> items, CancellationToken token)
    {
        if (items.Count == 0) return [];
        var classIds = items.Select(x => x.ClassId).Concat(items.Where(x => x.TargetClassId.HasValue).Select(x => x.TargetClassId!.Value))
            .Distinct().ToArray();
        var labels = (await timetable.ReadClassLabelsAsync(classIds, token)).ToDictionary(x => x.ClassId);
        var centers = await timetable.ReadCenterNamesAsync(items.Select(x => x.CenterId).Distinct().ToArray(), token);
        var students = await requests.ReadStudentsAsync(items.Select(x => x.StudentId).Distinct().ToArray(), token);
        var sessions = await requests.ReadSessionTimesAsync(items.Where(x => x.SessionId.HasValue)
            .Select(x => x.SessionId!.Value).Distinct().ToArray(), token);
        var reviewers = users is null ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(items.Where(x => x.ReviewedByUserId.HasValue).Select(x => x.ReviewedByUserId!.Value), token);
        return items.Select(x =>
        {
            var label = labels.GetValueOrDefault(x.ClassId);
            var target = x.TargetClassId is { } t ? labels.GetValueOrDefault(t) : null;
            var session = x.SessionId is { } s && sessions.TryGetValue(s, out var times) ? times : ((DateTimeOffset, DateTimeOffset)?)null;
            var student = students.TryGetValue(x.StudentId, out var row) ? row : ((string, string)?)null;
            return StudentRequestDto.From(x) with
            {
                CenterName = centers.GetValueOrDefault(x.CenterId),
                ClassCode = label?.Code, ClassName = label?.Name,
                TargetClassCode = target?.Code, TargetClassName = target?.Name,
                SessionStartUtc = session?.Item1, SessionEndUtc = session?.Item2,
                StudentCode = student?.Item1, StudentName = student?.Item2,
                ReviewedByName = x.ReviewedByUserId is { } reviewer ? reviewers.GetValueOrDefault(reviewer) : null
            };
        }).ToArray();
    }
}
