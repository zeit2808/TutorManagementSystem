using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class SessionScheduler(ITeachingStore store, IClock clock, ITeachingNameReader? names = null,
    ScheduleChangeRecorder? recorder = null)
{
    public Task<Result<SessionDto>> ScheduleAsync(Guid centerId, Guid classId, Guid teacherId,
        Guid roomId, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken token) =>
        ScheduleAsync(centerId, classId, teacherId, roomId, startUtc, endUtc, null, token);

    public Task<Result<SessionDto>> ScheduleAsync(Guid centerId, Guid classId, Guid teacherId,
        Guid roomId, DateTimeOffset startUtc, DateTimeOffset endUtc, Guid? makeupForSessionId, CancellationToken token) =>
        ScheduleAsync(centerId, classId, teacherId, roomId, startUtc, endUtc, makeupForSessionId, null, token);

    /// <summary>B7: students of the class are told about an extra or makeup session (same transaction).</summary>
    public async Task<Result<SessionDto>> ScheduleAsync(Guid centerId, Guid classId, Guid teacherId,
        Guid roomId, DateTimeOffset startUtc, DateTimeOffset endUtc, Guid? makeupForSessionId, Guid? actorUserId,
        CancellationToken token)
    {
        if (!TeachingSession.IsValidTime(startUtc, endUtc, clock.UtcNow))
            return Result<SessionDto>.Failure(TeachingErrors.Invalid);
        var teachingClass = await store.FindClassAsync(centerId, classId, token);
        if (teachingClass is null || !teachingClass.IsActive)
            return Result<SessionDto>.Failure(TeachingErrors.ClassUnavailable);
        var teacher = await store.FindTeacherAsync(centerId, teacherId, token);
        if (teacher is null || !teacher.IsActive)
            return Result<SessionDto>.Failure(TeachingErrors.TeacherUnavailable);
        if (!await store.RoomIsActiveAsync(centerId, roomId, token))
            return Result<SessionDto>.Failure(TeachingErrors.RoomUnavailable);

        var session = TeachingSession.Create(centerId, classId, teacherId, roomId, startUtc, endUtc, clock.UtcNow);
        if (makeupForSessionId is { } originalId)
        {
            // D6: a makeup replaces a cancelled or not-held session of the same class, once.
            var original = await store.FindSessionSnapshotAsync(centerId, originalId, token);
            if (original is null || original.ClassId != classId || original.IsActive)
                return Result<SessionDto>.Failure(TeachingErrors.InvalidMakeup);
            if (await store.HasActiveMakeupAsync(centerId, originalId, token))
                return Result<SessionDto>.Failure(TeachingErrors.MakeupExists);
            session.MarkAsMakeupFor(original);
        }
        try
        {
            var added = recorder is null
                ? await store.TryAddSessionAsync(session, token)
                : await store.TryAddSessionAsync(session, () => recorder.RecordAsync(
                    ScheduleChange.ForSession(session.MakeupForSessionId.HasValue
                            ? ScheduleChangeKind.MakeupScheduled : ScheduleChangeKind.SessionAdded,
                        session, null, null, null, null, null, actorUserId, clock.UtcNow),
                    clock.UtcNow, [(session.StartUtc, session.EndUtc)], session.Id, clock.UtcNow, token), token);
            if (!added) return Result<SessionDto>.Failure(TeachingErrors.ScheduleConflict);
        }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange); }
        catch (TeachingPersistenceException ex) when (ex.ConstraintName == "ux_sessions_makeup_for")
        { return Result<SessionDto>.Failure(TeachingErrors.MakeupExists); }
        return Result<SessionDto>.Success(SessionDto.From(session));
    }

    /// <summary>Kept for callers without a reason; records the generic series-change reason.</summary>
    public Task<Result<SessionDto>> CancelAsync(Guid centerId, Guid sessionId,
        int expectedVersion, CancellationToken token) =>
        CancelAsync(centerId, sessionId, expectedVersion, TeachingSession.SeriesStoppedReason, null, token);

    public async Task<Result<SessionDto>> CancelAsync(Guid centerId, Guid sessionId,
        int expectedVersion, string reason, Guid? actorUserId, CancellationToken token)
    {
        if (!TeachingSession.IsValidReason(reason)) return Result<SessionDto>.Failure(TeachingErrors.InvalidSessionReason);
        var snapshot = await store.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [snapshot.ClassId], [snapshot.TeacherId], [snapshot.RoomId], token);
        var session = await store.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        if (session.Version != expectedVersion) return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!session.IsActive || session.StartUtc <= clock.UtcNow)
            return Result<SessionDto>.Failure(TeachingErrors.Invalid);
        if (await store.SessionHasAttendanceAsync(centerId, sessionId, token))
            return Result<SessionDto>.Failure(TeachingErrors.AttendanceExists);
        session.Cancel(clock.UtcNow, reason, actorUserId);
        if (recorder is not null)
            await recorder.RecordAsync(ScheduleChange.ForSession(ScheduleChangeKind.SessionCancelled, session,
                session.StartUtc, session.EndUtc, session.TeacherId, session.RoomId, reason, actorUserId, clock.UtcNow),
                clock.UtcNow, [], session.Id, clock.UtcNow, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<SessionDto>.Success(SessionDto.From(session));
    }

    /// <summary>
    /// D6: records that a session which has already started did not take place. Attendance must not exist;
    /// the session can then get a makeup session.
    /// </summary>
    public async Task<Result<SessionDto>> MarkNotHeldAsync(Guid centerId, Guid sessionId, int expectedVersion,
        string reason, Guid? actorUserId, CancellationToken token)
    {
        if (!TeachingSession.IsValidReason(reason)) return Result<SessionDto>.Failure(TeachingErrors.InvalidSessionReason);
        var snapshot = await store.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [snapshot.ClassId], [snapshot.TeacherId], [snapshot.RoomId], token);
        var session = await store.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        if (session.Version != expectedVersion) return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!session.IsActive) return Result<SessionDto>.Failure(TeachingErrors.CancelledAttendanceSession);
        if (session.StartUtc > clock.UtcNow) return Result<SessionDto>.Failure(TeachingErrors.SessionNotStarted);
        if (await store.SessionHasAttendanceAsync(centerId, sessionId, token))
            return Result<SessionDto>.Failure(TeachingErrors.AttendanceExists);
        session.MarkNotHeld(clock.UtcNow, reason, actorUserId);
        if (recorder is not null)
            await recorder.RecordAsync(ScheduleChange.ForSession(ScheduleChangeKind.SessionNotHeld, session,
                session.StartUtc, session.EndUtc, session.TeacherId, session.RoomId, reason, actorUserId, clock.UtcNow),
                session.StartUtc, [], session.Id, clock.UtcNow, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<SessionDto>.Success(SessionDto.From(session));
    }

    public Task<Result<SessionDto>> RescheduleAsync(Guid centerId, Guid sessionId,
        Guid teacherId, Guid roomId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        int expectedVersion, CancellationToken token) =>
        RescheduleAsync(centerId, sessionId, teacherId, roomId, startUtc, endUtc, expectedVersion,
            null, null, token);

    public async Task<Result<SessionDto>> RescheduleAsync(Guid centerId, Guid sessionId,
        Guid teacherId, Guid roomId, DateTimeOffset startUtc, DateTimeOffset endUtc,
        int expectedVersion, string? reason, Guid? actorUserId, CancellationToken token)
    {
        if (reason is not null && !TeachingSession.IsValidReason(reason))
            return Result<SessionDto>.Failure(TeachingErrors.InvalidSessionReason);
        if (!TeachingSession.IsValidTime(startUtc, endUtc, clock.UtcNow))
            return Result<SessionDto>.Failure(TeachingErrors.Invalid);
        var snapshot = await store.FindSessionSnapshotAsync(centerId, sessionId, token);
        if (snapshot is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [snapshot.ClassId], [snapshot.TeacherId, teacherId],
            [snapshot.RoomId, roomId], token);
        var session = await store.FindSessionAsync(centerId, sessionId, token);
        if (session is null) return Result<SessionDto>.Failure(TeachingErrors.NotFound);
        if (session.Version != expectedVersion)
            return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange);
        if (!session.IsActive || session.StartUtc <= clock.UtcNow)
            return Result<SessionDto>.Failure(TeachingErrors.StartedSession);
        if (await store.SessionHasAttendanceAsync(centerId, sessionId, token))
            return Result<SessionDto>.Failure(TeachingErrors.AttendanceExists);
        var teacher = await store.FindTeacherAsync(centerId, teacherId, token);
        if (teacher is null || !teacher.IsActive)
            return Result<SessionDto>.Failure(TeachingErrors.TeacherUnavailable);
        if (!await store.RoomIsActiveAsync(centerId, roomId, token))
            return Result<SessionDto>.Failure(TeachingErrors.RoomUnavailable);
        var conflicts = await store.ListResourceSessionsAsync(centerId, session.ClassId,
            teacherId, roomId, startUtc, endUtc, token);
        if (conflicts.Any(x => x.Id != sessionId))
            return Result<SessionDto>.Failure(TeachingErrors.ScheduleConflict);
        var (oldStart, oldEnd, oldTeacher, oldRoom) = (session.StartUtc, session.EndUtc, session.TeacherId, session.RoomId);
        if (reason is null) session.Reschedule(teacherId, roomId, startUtc, endUtc, clock.UtcNow);
        else session.Reschedule(teacherId, roomId, startUtc, endUtc, clock.UtcNow, reason, actorUserId);
        if (recorder is not null)
            await recorder.RecordAsync(ScheduleChange.ForSession(ScheduleChangeKind.SessionRescheduled, session,
                oldStart, oldEnd, oldTeacher, oldRoom, reason, actorUserId, clock.UtcNow),
                clock.UtcNow, [(startUtc, endUtc)], session.Id, clock.UtcNow, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) when (ex.IsScheduleConflict)
        { return Result<SessionDto>.Failure(TeachingErrors.ScheduleConflict); }
        catch (TeachingPersistenceException ex) when (ex.IsConcurrency)
        { return Result<SessionDto>.Failure(TeachingErrors.ConcurrentChange); }
        await guard.CommitAsync(token);
        return Result<SessionDto>.Success(SessionDto.From(session));
    }

    public Task<Result<IReadOnlyList<SessionDto>>> ListAsync(Guid centerId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token) =>
        ListAsync(centerId, fromUtc, toUtc, SessionFilter.None, token);

    public async Task<Result<IReadOnlyList<SessionDto>>> ListAsync(Guid centerId,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, SessionFilter filter, CancellationToken token)
    {
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero || toUtc <= fromUtc ||
            toUtc - fromUtc > TimeSpan.FromDays(31))
            return Result<IReadOnlyList<SessionDto>>.Failure(TeachingErrors.Invalid);
        var items = await store.ListSessionsAsync(centerId, fromUtc, toUtc, filter, token);
        var lookup = names is null || items.Count == 0 ? TeachingNames.Empty
            : await names.ReadAsync(centerId, items.Select(x => x.ClassId), items.Select(x => x.TeacherId),
                items.Select(x => x.RoomId), token);
        var moved = items.Where(x => x.RescheduledAtUtc.HasValue).Select(x => x.Id).ToArray();
        var previous = moved.Length == 0
            ? new Dictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)>()
            : await store.ReadPreviousTimesAsync(centerId, moved, token);
        return Result<IReadOnlyList<SessionDto>>.Success(items.Select(x =>
        {
            var dto = SessionDto.From(x).WithNames(lookup);
            if (previous.TryGetValue(x.Id, out var times))
            {
                dto.PreviousStartUtc = times.Start;
                dto.PreviousEndUtc = times.End;
            }
            return dto;
        }).ToArray());
    }
}
