using System.Text.Json;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>
/// Who is acting on a sheet. Managers (owner/admin) may open any session; everyone else only the
/// sessions their linked teacher profile is assigned to or recorded on, and may write only until the
/// end of that session's local day (decision D3). Corrections stay manager-only at the endpoint.
/// </summary>
public sealed record AttendanceActor(Guid UserId, bool CanManage)
{
    public static AttendanceActor Manager(Guid userId) => new(userId, true);
}

public sealed class AttendanceWorkflow(ITeachingStore teaching, IAttendanceStore attendance, IClock clock,
    ITeachingNameReader? names = null, IUserDirectory? users = null)
{
    public Task<Result<AttendanceSheetDto>> GetAsync(Guid centerId, Guid sessionId, CancellationToken token) =>
        GetAsync(centerId, sessionId, AttendanceActor.Manager(Guid.Empty), token);

    public async Task<Result<AttendanceSheetDto>> GetAsync(Guid centerId, Guid sessionId, AttendanceActor actor,
        CancellationToken token)
    {
        var session = await teaching.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (session is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        var sheet = await attendance.FindSheetAsync(centerId, sessionId, token);
        var scope = await CheckScopeAsync(centerId, actor, session, sheet, write: false, token);
        if (scope != Error.None) return Result<AttendanceSheetDto>.Failure(scope);
        var roster = await attendance.ListRosterAsync(centerId, session.ClassId, session.StartUtc, token);
        var entries = sheet is null ? [] : await attendance.ListEntriesAsync(centerId, sessionId, token);
        var excused = await attendance.ListApprovedAbsencesAsync(centerId, sessionId, token);
        var dto = ToDto(session, roster, sheet, entries, excused);
        if (names is not null)
        {
            var lookup = await names.ReadAsync(centerId, [session.ClassId],
                dto.ActualTeacherId.HasValue ? new[] { dto.ActualTeacherId.Value } : Array.Empty<Guid>(), [], token);
            dto = dto.WithNames(lookup);
        }
        return Result<AttendanceSheetDto>.Success(dto);
    }

    public Task<Result<AttendanceSheetDto>> SaveDraftAsync(Guid centerId, Guid sessionId,
        SaveAttendanceDraftRequest request, Guid actorId, CancellationToken token) =>
        SaveDraftAsync(centerId, sessionId, request, AttendanceActor.Manager(actorId), token);

    public async Task<Result<AttendanceSheetDto>> SaveDraftAsync(Guid centerId, Guid sessionId,
        SaveAttendanceDraftRequest request, AttendanceActor actor, CancellationToken token)
    {
        var actorId = actor.UserId;
        if (!TryMark(request.DefaultMark, out var defaultMark) ||
            !ValidateOverrides(request.Overrides, out var overrides))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.InvalidAttendance);
        var snapshot = await teaching.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await teaching.BeginGuardAsync(centerId, snapshot.ClassId, null, null, null, token);
        var session = await teaching.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        if (!session.IsActive) return Result<AttendanceSheetDto>.Failure(TeachingErrors.CancelledAttendanceSession);
        if (session.StartUtc > clock.UtcNow) return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceNotStarted);
        var sheet = await attendance.FindSheetAsync(centerId, sessionId, token);
        var scope = await CheckScopeAsync(centerId, actor, session, sheet, write: true, token);
        if (scope != Error.None) return Result<AttendanceSheetDto>.Failure(scope);
        if (sheet?.Status == AttendanceSheetStatus.Submitted)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceSubmitted);
        if ((sheet?.Version ?? 0) != request.ExpectedVersion)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange);
        if (request.ActualTeacherId.HasValue &&
            !await CanBeActualTeacherAsync(centerId, session, sheet, request.ActualTeacherId.Value, token))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.TeacherUnavailable);
        var roster = await attendance.ListRosterAsync(centerId, session.ClassId, session.StartUtc, token);
        if (roster.Count == 0 || overrides.Keys.Except(roster.Select(x => x.StudentId)).Any())
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.InvalidAttendanceRoster);
        var excused = await attendance.ListApprovedAbsencesAsync(centerId, sessionId, token);
        var entries = sheet is null ? [] : await attendance.ListEntriesAsync(centerId, sessionId, token);
        if (sheet is not null && entries.Count == roster.Count &&
            sheet.ActualTeacherId == request.ActualTeacherId &&
            sheet.Note == SessionAttendance.NormalizeNote(request.Note) &&
            entries.All(entry =>
            {
                var choice = overrides.GetValueOrDefault(entry.StudentId);
                return entry.Mark == (choice is null ? defaultMark : Enum.Parse<AttendanceMark>(choice.Mark, true)) &&
                    entry.Note == SessionAttendance.NormalizeNote(choice?.Note);
            }))
        {
            await guard.CommitAsync(token);
            return Result<AttendanceSheetDto>.Success(ToDto(session, roster, sheet, entries, excused));
        }
        sheet ??= SessionAttendance.Create(centerId, sessionId, session.ClassId, clock.UtcNow, actorId);
        var existing = entries.ToDictionary(x => x.StudentId);
        var added = new List<AttendanceEntry>();
        foreach (var student in roster)
        {
            var choice = overrides.GetValueOrDefault(student.StudentId);
            // B8: an approved absence request fills "Vắng có phép" unless the teacher chose a mark.
            var excusedReason = choice is null && !existing.ContainsKey(student.StudentId) ? excused.GetValueOrDefault(student.StudentId) : null;
            var mark = choice is not null ? Enum.Parse<AttendanceMark>(choice.Mark, true)
                : excusedReason is not null ? AttendanceMark.ExcusedAbsent : defaultMark;
            var note = choice?.Note ?? (excusedReason is null ? null : ExcusedNote(excusedReason));
            if (existing.TryGetValue(student.StudentId, out var entry))
                entry.Change(mark, note, clock.UtcNow);
            else
                added.Add(AttendanceEntry.Create(centerId, sessionId, student.EnrollmentId,
                    student.StudentId, mark, note, clock.UtcNow));
        }
        sheet.SaveDraft(request.ActualTeacherId, request.Note, clock.UtcNow, actorId);
        if (request.ExpectedVersion == 0) await attendance.AddSheetAsync(sheet, token);
        await attendance.AddEntriesAsync(added, token);
        var all = entries.Concat(added).ToArray();
        // Decision D11: drafts keep only their latest state; history records Submitted and Corrected.
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<AttendanceSheetDto>.Success(ToDto(session, roster, sheet, all, excused));
    }

    public Task<Result<AttendanceSheetDto>> SubmitAsync(Guid centerId, Guid sessionId,
        int expectedVersion, Guid actorId, CancellationToken token) =>
        SubmitAsync(centerId, sessionId, expectedVersion, AttendanceActor.Manager(actorId), token);

    public async Task<Result<AttendanceSheetDto>> SubmitAsync(Guid centerId, Guid sessionId,
        int expectedVersion, AttendanceActor actor, CancellationToken token)
    {
        var actorId = actor.UserId;
        var snapshot = await teaching.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await teaching.BeginGuardAsync(centerId, snapshot.ClassId, null, null, null, token);
        var session = await teaching.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        var sheet = await attendance.FindSheetAsync(centerId, sessionId, token);
        var scope = await CheckScopeAsync(centerId, actor, session, sheet, write: true, token);
        if (scope != Error.None) return Result<AttendanceSheetDto>.Failure(scope);
        if (sheet is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceDraftRequired);
        if (sheet.Version != expectedVersion) return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange);
        if (sheet.Status != AttendanceSheetStatus.Draft)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceSubmitted);
        var roster = await attendance.ListRosterAsync(centerId, session.ClassId, session.StartUtc, token);
        var entries = await attendance.ListEntriesAsync(centerId, sessionId, token);
        if (sheet.ActualTeacherId is null || entries.Count != roster.Count ||
            entries.Any(x => x.Mark == AttendanceMark.Unknown) || roster.Count == 0)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceIncomplete);
        var before = Snapshot(sheet, entries);
        sheet.Submit(clock.UtcNow, actorId);
        await attendance.AddRevisionAsync(AttendanceRevision.Create(centerId, sessionId, sheet.Version,
            "Submitted", actorId, null, before, Snapshot(sheet, entries), clock.UtcNow), token);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<AttendanceSheetDto>.Success(ToDto(session, roster, sheet, entries));
    }

    public async Task<Result<AttendanceSheetDto>> CorrectAsync(Guid centerId, Guid sessionId,
        CorrectAttendanceRequest request, Guid actorId, CancellationToken token)
    {
        if (request.Reason is null || request.Reason.Trim().Length is < 10 or > 500 ||
            !ValidateOverrides(request.Changes, out var changes))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.InvalidAttendance);
        var snapshot = await teaching.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await teaching.BeginGuardAsync(centerId, snapshot.ClassId, null, null, null, token);
        var session = await teaching.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<AttendanceSheetDto>.Failure(TeachingErrors.NotFound);
        var sheet = await attendance.FindSheetAsync(centerId, sessionId, token);
        if (sheet is null || sheet.Status != AttendanceSheetStatus.Submitted)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceDraftRequired);
        if (sheet.Version != request.ExpectedVersion)
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!await CanBeActualTeacherAsync(centerId, session, sheet, request.ActualTeacherId, token))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.TeacherUnavailable);
        var roster = await attendance.ListRosterAsync(centerId, session.ClassId, session.StartUtc, token);
        var entries = await attendance.ListEntriesAsync(centerId, sessionId, token);
        var byStudent = entries.ToDictionary(x => x.StudentId);
        if (changes.Keys.Except(byStudent.Keys).Any() ||
            changes.Values.Any(x => x.Mark.Equals(nameof(AttendanceMark.Unknown), StringComparison.OrdinalIgnoreCase)))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.InvalidAttendanceRoster);
        if (sheet.ActualTeacherId == request.ActualTeacherId &&
            sheet.Note == SessionAttendance.NormalizeNote(request.Note) &&
            changes.All(x => byStudent[x.Key].Mark == Enum.Parse<AttendanceMark>(x.Value.Mark, true) &&
                byStudent[x.Key].Note == SessionAttendance.NormalizeNote(x.Value.Note)))
            return Result<AttendanceSheetDto>.Failure(TeachingErrors.AttendanceUnchanged);
        var before = Snapshot(sheet, entries);
        foreach (var (studentId, change) in changes)
            byStudent[studentId].Change(Enum.Parse<AttendanceMark>(change.Mark, true), change.Note, clock.UtcNow);
        sheet.Correct(request.ActualTeacherId, request.Note, clock.UtcNow, actorId);
        await attendance.AddRevisionAsync(AttendanceRevision.Create(centerId, sessionId, sheet.Version,
            "Corrected", actorId, request.Reason.Trim(), before, Snapshot(sheet, entries), clock.UtcNow), token);
        try { await teaching.SaveAsync(token); }
        catch (TeachingPersistenceException) { return Result<AttendanceSheetDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<AttendanceSheetDto>.Success(ToDto(session, roster, sheet, entries));
    }

    public async Task<Result<IReadOnlyList<AttendanceRevisionDto>>> HistoryAsync(Guid centerId,
        Guid sessionId, CancellationToken token)
    {
        if (await teaching.FindSessionSnapshotAsync(centerId, sessionId, token) is null)
            return Result<IReadOnlyList<AttendanceRevisionDto>>.Failure(TeachingErrors.NotFound);
        // Older databases still hold per-draft rows; D11 shows only submissions and corrections.
        var revisions = (await attendance.ListRevisionsAsync(centerId, sessionId, token))
            .Where(x => x.Action != "DraftSaved").ToArray();
        var actorNames = users is null ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(revisions.Select(x => x.ActorUserId), token);
        return Result<IReadOnlyList<AttendanceRevisionDto>>.Success(revisions.Select(x =>
            new AttendanceRevisionDto(x.Version, x.Action, x.ActorUserId, x.Reason,
                x.CreatedAtUtc, JsonSerializer.Deserialize<AttendanceSnapshotDto>(x.BeforeJson)!,
                JsonSerializer.Deserialize<AttendanceSnapshotDto>(x.AfterJson)!)
            { ActorName = actorNames.GetValueOrDefault(x.ActorUserId) }).ToArray());
    }

    private async Task<Error> CheckScopeAsync(Guid centerId, AttendanceActor actor, TeachingSession session,
        SessionAttendance? sheet, bool write, CancellationToken token)
    {
        if (actor.CanManage) return Error.None;
        var profile = await teaching.FindTeacherByUserAsync(centerId, actor.UserId, token);
        if (profile is null || !profile.IsActive ||
            (session.TeacherId != profile.Id && sheet?.ActualTeacherId != profile.Id))
            return TeachingErrors.AttendanceNotAssigned;
        if (write && clock.UtcNow >= CenterTime.EndOfLocalDay(session.StartUtc))
            return TeachingErrors.AttendanceWindowClosed;
        return Error.None;
    }

    /// <summary>
    /// A substitute must be an active teacher. Archived teachers stay acceptable only when they are
    /// already on record for this session (assigned, or the actual teacher saved earlier), so a
    /// teacher archived after teaching does not block finishing or correcting that sheet.
    /// </summary>
    private async Task<bool> CanBeActualTeacherAsync(Guid centerId, TeachingSession session,
        SessionAttendance? sheet, Guid teacherId, CancellationToken token)
    {
        var teacher = await teaching.FindTeacherAsync(centerId, teacherId, token);
        return teacher is not null &&
            (teacher.IsActive || teacherId == session.TeacherId || teacherId == sheet?.ActualTeacherId);
    }

    private static string ExcusedNote(string reason)
    {
        var note = $"Xin vắng đã duyệt: {reason}";
        return note.Length <= 500 ? note : note[..500];
    }

    private static AttendanceSheetDto ToDto(TeachingSession session, IReadOnlyList<AttendanceRosterRow> roster,
        SessionAttendance? sheet, IReadOnlyList<AttendanceEntry> entries,
        IReadOnlyDictionary<Guid, string>? excused = null)
    {
        var byStudent = entries.ToDictionary(x => x.StudentId);
        return new(session.Id, session.ClassId, session.StartUtc,
            sheet?.Status.ToString() ?? (session.IsActive ? "NotStarted" : "Cancelled"),
            sheet?.Version ?? 0, sheet?.ActualTeacherId, sheet?.Note, sheet?.SubmittedAtUtc,
            roster.Select(x =>
            {
                if (byStudent.TryGetValue(x.StudentId, out var entry))
                    return new AttendanceStudentDto(x.StudentId, x.StudentCode, x.FullName, entry.Mark.ToString(), entry.Note)
                    {
                        ExcusedAbsenceApproved = excused?.ContainsKey(x.StudentId) == true
                    };
                // B8: an approved absence request is shown pre-filled until a mark is saved.
                return excused?.TryGetValue(x.StudentId, out var reason) == true
                    ? new AttendanceStudentDto(x.StudentId, x.StudentCode, x.FullName,
                        nameof(AttendanceMark.ExcusedAbsent), ExcusedNote(reason!)) { ExcusedAbsenceApproved = true }
                    : new AttendanceStudentDto(x.StudentId, x.StudentCode, x.FullName, "Unknown", null);
            }).ToArray());
    }

    private static string Snapshot(SessionAttendance? sheet, IReadOnlyList<AttendanceEntry> entries) =>
        JsonSerializer.Serialize(new { Status = sheet?.Status.ToString() ?? "NotStarted",
            sheet?.ActualTeacherId, sheet?.Note,
            Students = entries.OrderBy(x => x.StudentId).Select(x => new { x.StudentId,
                Mark = x.Mark.ToString(), x.Note }).ToArray() });

    private static bool TryMark(string? value, out AttendanceMark mark) =>
        Enum.TryParse(value, true, out mark) && Enum.IsDefined(mark) &&
        Enum.GetNames<AttendanceMark>().Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase));

    private static bool ValidateOverrides(IReadOnlyList<AttendanceOverrideRequest>? input,
        out Dictionary<Guid, AttendanceOverrideRequest> result)
    {
        result = new();
        if (input is null || input.Count > 500) return false;
        foreach (var item in input)
        {
            if (item.StudentId == Guid.Empty || !TryMark(item.Mark, out _) ||
                item.Note?.Length > 500 || !result.TryAdd(item.StudentId, item)) return false;
        }
        return true;
    }
}
