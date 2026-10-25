using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class RecurringScheduleWorkflow(ITeachingStore store, IClock clock,
    ITeachingNameReader? names = null, ScheduleChangeRecorder? recorder = null)
{
    private sealed record Occurrence(DateOnly Date, DateTimeOffset StartUtc, DateTimeOffset EndUtc);
    private sealed record Prepared(TimeZoneInfo Zone, int WeekdaysMask, DateOnly[] ExcludedDates,
        IReadOnlyList<Occurrence> Occurrences, string RequestHash);

    /// <summary>B12/D12: series always run on Vietnam time; an omitted zone means Asia/Ho_Chi_Minh.</summary>
    private static CreateScheduleSeriesRequest WithCenterZone(CreateScheduleSeriesRequest request) =>
        request is not null && string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? request with { TimeZoneId = CenterTime.ZoneId }
            : request!;

    public async Task<Result<SchedulePreviewDto>> PreviewAsync(Guid centerId,
        CreateScheduleSeriesRequest request, CancellationToken token)
    {
        request = WithCenterZone(request);
        var prepared = Prepare(request, null, null);
        if (prepared.IsFailure) return Result<SchedulePreviewDto>.Failure(prepared.Error);
        var timingError = ValidateTiming(prepared.Value, clock.UtcNow);
        if (timingError != Error.None) return Result<SchedulePreviewDto>.Failure(timingError);
        var error = await ValidateResourcesAsync(centerId, request, token);
        if (error != Error.None) return Result<SchedulePreviewDto>.Failure(error);
        var conflicts = await FindConflictsAsync(centerId, request, prepared.Value.Occurrences, token);
        return Result<SchedulePreviewDto>.Success(new(prepared.Value.Occurrences.Count, conflicts));
    }

    public async Task<Result<ScheduleSeriesCreatedDto>> CreateAsync(Guid centerId,
        CreateScheduleSeriesRequest request, CancellationToken token)
    {
        request = WithCenterZone(request);
        var prepared = Prepare(request, null, null);
        if (prepared.IsFailure) return Result<ScheduleSeriesCreatedDto>.Failure(prepared.Error);
        var existing = await store.FindSeriesByRequestIdAsync(centerId, request.RequestId, token);
        if (existing is not null)
            return existing.RequestHash == prepared.Value.RequestHash
                ? Result<ScheduleSeriesCreatedDto>.Success(Created(existing, true))
                : Result<ScheduleSeriesCreatedDto>.Failure(TeachingErrors.RequestIdReused);
        var timingError = ValidateTiming(prepared.Value, clock.UtcNow);
        if (timingError != Error.None) return Result<ScheduleSeriesCreatedDto>.Failure(timingError);

        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [request.ClassId], [request.TeacherId], [request.RoomId], token);
        existing = await store.FindSeriesByRequestIdAsync(centerId, request.RequestId, token);
        if (existing is not null)
            return existing.RequestHash == prepared.Value.RequestHash
                ? Result<ScheduleSeriesCreatedDto>.Success(Created(existing, true))
                : Result<ScheduleSeriesCreatedDto>.Failure(TeachingErrors.RequestIdReused);
        var now = clock.UtcNow;
        timingError = ValidateTiming(prepared.Value, now);
        if (timingError != Error.None) return Result<ScheduleSeriesCreatedDto>.Failure(timingError);
        var error = await ValidateResourcesAsync(centerId, request, token);
        if (error != Error.None) return Result<ScheduleSeriesCreatedDto>.Failure(error);
        var conflicts = await FindConflictsAsync(centerId, request, prepared.Value.Occurrences, token);
        if (conflicts.Any(x => x.Conflicts.Count > 0))
            return Result<ScheduleSeriesCreatedDto>.Failure(TeachingErrors.ScheduleConflict);

        var series = NewSeries(centerId, request, prepared.Value, null, 0, now);
        var sessions = NewSessions(centerId, request, series.Id, prepared.Value.Occurrences, now);
        await store.AddAsync(series, token);
        await store.AddSessionsAsync(sessions, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) { return Result<ScheduleSeriesCreatedDto>.Failure(PersistenceError(ex)); }
        await guard.CommitAsync(token);
        return Result<ScheduleSeriesCreatedDto>.Success(Created(series, false));
    }

    public async Task<TeachingPage<ScheduleSeriesDto>> ListAsync(Guid centerId,
        Guid? classId, int page, int pageSize, CancellationToken token)
    {
        var (items, totalCount) = await store.ListSeriesAsync(centerId, classId, page, pageSize, token);
        var lookup = await ReadNamesAsync(centerId, items, token);
        return new TeachingPage<ScheduleSeriesDto>
        {
            Page = page, PageSize = pageSize, TotalCount = totalCount,
            Items = items.Select(x => ScheduleSeriesDto.From(x).WithNames(lookup)).ToList()
        };
    }

    public async Task<Result<ScheduleSeriesDto>> GetAsync(Guid centerId, Guid seriesId, CancellationToken token)
    {
        var series = await store.FindSeriesAsync(centerId, seriesId, token);
        if (series is null) return Result<ScheduleSeriesDto>.Failure(TeachingErrors.NotFound);
        var lookup = await ReadNamesAsync(centerId, [series], token);
        return Result<ScheduleSeriesDto>.Success(ScheduleSeriesDto.From(series).WithNames(lookup));
    }

    private Task<TeachingNames> ReadNamesAsync(Guid centerId, IReadOnlyCollection<ScheduleSeries> items,
        CancellationToken token) => names is null || items.Count == 0
        ? Task.FromResult(TeachingNames.Empty)
        : names.ReadAsync(centerId, items.Select(x => x.ClassId), items.Select(x => x.TeacherId),
            items.Select(x => x.RoomId), token);

    public Task<Result<ScheduleSeriesStoppedDto>> StopAsync(Guid centerId, Guid seriesId,
        DateOnly fromDate, int expectedVersion, CancellationToken token) =>
        StopAsync(centerId, seriesId, fromDate, expectedVersion, TeachingSession.SeriesStoppedReason, true, null, token);

    /// <summary>
    /// B6: stopping needs a reason. If the first affected session is less than 7 days away the change must be
    /// confirmed (<paramref name="acknowledgeShortNotice"/>) and is flagged urgent for students.
    /// </summary>
    public async Task<Result<ScheduleSeriesStoppedDto>> StopAsync(Guid centerId, Guid seriesId,
        DateOnly fromDate, int expectedVersion, string reason, bool acknowledgeShortNotice, Guid? actorUserId,
        CancellationToken token)
    {
        if (!TeachingSession.IsValidReason(reason)) return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.InvalidSessionReason);
        var snapshot = await store.FindSeriesAsync(centerId, seriesId, token);
        if (snapshot is null) return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.NotFound);
        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [snapshot.ClassId], [snapshot.TeacherId], [snapshot.RoomId], token);
        var series = await store.FindSeriesForUpdateAsync(centerId, seriesId, token);
        if (series is null) return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.NotFound);
        if (series.Version != expectedVersion) return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.ConcurrentChange);
        var cutoffError = ValidateCutoff(series, fromDate);
        if (cutoffError != Error.None) return Result<ScheduleSeriesStoppedDto>.Failure(cutoffError);
        var sessions = await store.ListSeriesSessionsAsync(centerId, seriesId, fromDate, token);
        if (sessions.Any(x => x.IsActive && x.StartUtc <= clock.UtcNow))
            return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.StartedSession);
        var active = sessions.Where(x => x.IsActive).ToArray();
        if (await store.AnySessionHasAttendanceAsync(centerId, active.Select(x => x.Id).ToArray(), token))
            return Result<ScheduleSeriesStoppedDto>.Failure(TeachingErrors.AttendanceExists);
        var now = clock.UtcNow;
        DateTimeOffset? first = active.Length == 0 ? null : active.Min(x => x.StartUtc);
        if (ScheduleChange.IsShortNotice(first, now) && !acknowledgeShortNotice)
            return Result<ScheduleSeriesStoppedDto>.Failure(ScheduleChangeErrors.ShortNotice);
        foreach (var session in active) session.Cancel(now, reason, actorUserId);
        series.StopFrom(fromDate);
        if (recorder is not null && active.Length > 0)
            await recorder.RecordAsync(ScheduleChange.ForSeries(ScheduleChangeKind.SeriesStopped, centerId, series.ClassId,
                series.Id, active.Length, first, null, reason, actorUserId, now), now, [], null, now, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) { return Result<ScheduleSeriesStoppedDto>.Failure(PersistenceError(ex)); }
        await guard.CommitAsync(token);
        return Result<ScheduleSeriesStoppedDto>.Success(new(ScheduleSeriesDto.From(series), active.Length));
    }

    public Task<Result<ScheduleSeriesReplacedDto>> ReplaceFutureAsync(Guid centerId,
        Guid seriesId, ReplaceScheduleSeriesRequest request, CancellationToken token) =>
        ReplaceFutureAsync(centerId, seriesId, request, null, token);

    /// <summary>B6: like <see cref="StopAsync(Guid, Guid, DateOnly, int, string, bool, Guid?, CancellationToken)"/>,
    /// a reason is required and less than 7 days' notice must be confirmed.</summary>
    public async Task<Result<ScheduleSeriesReplacedDto>> ReplaceFutureAsync(Guid centerId,
        Guid seriesId, ReplaceScheduleSeriesRequest request, Guid? actorUserId, CancellationToken token)
    {
        if (request is null) return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.InvalidSeries);
        var reason = request.Reason ?? TeachingSession.SeriesStoppedReason;
        if (!TeachingSession.IsValidReason(reason)) return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.InvalidSessionReason);
        if (request.Replacement is not null) request = request with { Replacement = WithCenterZone(request.Replacement) };
        var snapshot = await store.FindSeriesAsync(centerId, seriesId, token);
        if (snapshot is null) return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.NotFound);
        if (request.Replacement is null || request.Replacement.StartDate != request.FromDate ||
            request.Replacement.ClassId != snapshot.ClassId ||
            !string.Equals(request.Replacement.TimeZoneId, snapshot.TimeZoneId, StringComparison.OrdinalIgnoreCase))
            return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.InvalidSeries);
        var prepared = Prepare(request.Replacement, seriesId, request.FromDate);
        if (prepared.IsFailure) return Result<ScheduleSeriesReplacedDto>.Failure(prepared.Error);
        var replay = await store.FindSeriesByRequestIdAsync(centerId, request.Replacement.RequestId, token);
        if (replay is not null)
            return replay.RequestHash == prepared.Value.RequestHash && replay.ReplacesSeriesId == seriesId
                ? Result<ScheduleSeriesReplacedDto>.Success(new(ScheduleSeriesDto.From(snapshot),
                    Created(replay, true), replay.ReplacedSessionsCancelled))
                : Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.RequestIdReused);
        var timingError = ValidateTiming(prepared.Value, clock.UtcNow);
        if (timingError != Error.None) return Result<ScheduleSeriesReplacedDto>.Failure(timingError);

        await using var guard = await store.BeginScheduleGuardAsync(centerId,
            [snapshot.ClassId], [snapshot.TeacherId, request.Replacement.TeacherId],
            [snapshot.RoomId, request.Replacement.RoomId], token);
        var series = await store.FindSeriesForUpdateAsync(centerId, seriesId, token);
        if (series is null) return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.NotFound);
        replay = await store.FindSeriesByRequestIdAsync(centerId, request.Replacement.RequestId, token);
        if (replay is not null)
            return replay.RequestHash == prepared.Value.RequestHash && replay.ReplacesSeriesId == seriesId
                ? Result<ScheduleSeriesReplacedDto>.Success(new(ScheduleSeriesDto.From(series),
                    Created(replay, true), replay.ReplacedSessionsCancelled))
                : Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.RequestIdReused);
        if (series.Version != request.ExpectedVersion)
            return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.ConcurrentChange);
        var cutoffError = ValidateCutoff(series, request.FromDate);
        if (cutoffError != Error.None) return Result<ScheduleSeriesReplacedDto>.Failure(cutoffError);
        var error = await ValidateResourcesAsync(centerId, request.Replacement, token);
        if (error != Error.None) return Result<ScheduleSeriesReplacedDto>.Failure(error);
        var sessions = await store.ListSeriesSessionsAsync(centerId, seriesId, request.FromDate, token);
        if (sessions.Any(x => x.IsActive && x.StartUtc <= clock.UtcNow))
            return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.StartedSession);
        var active = sessions.Where(x => x.IsActive).ToArray();
        if (await store.AnySessionHasAttendanceAsync(centerId, active.Select(x => x.Id).ToArray(), token))
            return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.AttendanceExists);
        DateTimeOffset? firstAffected = active.Select(x => x.StartUtc)
            .Concat(prepared.Value.Occurrences.Select(x => x.StartUtc)).DefaultIfEmpty().Min();
        if (firstAffected == default(DateTimeOffset)) firstAffected = null;
        if (ScheduleChange.IsShortNotice(firstAffected, clock.UtcNow) && request.AcknowledgeShortNotice != true)
            return Result<ScheduleSeriesReplacedDto>.Failure(ScheduleChangeErrors.ShortNotice);
        foreach (var session in active) session.Cancel(clock.UtcNow, reason, actorUserId);
        series.StopFrom(request.FromDate);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) { return Result<ScheduleSeriesReplacedDto>.Failure(PersistenceError(ex)); }

        var conflicts = await FindConflictsAsync(centerId, request.Replacement,
            prepared.Value.Occurrences, token);
        if (conflicts.Any(x => x.Conflicts.Count > 0))
            return Result<ScheduleSeriesReplacedDto>.Failure(TeachingErrors.ScheduleConflict);
        var now = clock.UtcNow;
        timingError = ValidateTiming(prepared.Value, now);
        if (timingError != Error.None) return Result<ScheduleSeriesReplacedDto>.Failure(timingError);
        var replacement = NewSeries(centerId, request.Replacement, prepared.Value,
            seriesId, active.Length, now);
        await store.AddAsync(replacement, token);
        await store.AddSessionsAsync(NewSessions(centerId, request.Replacement,
            replacement.Id, prepared.Value.Occurrences, now), token);
        if (recorder is not null)
            await recorder.RecordAsync(ScheduleChange.ForSeries(ScheduleChangeKind.SeriesReplaced, centerId, series.ClassId,
                    series.Id, active.Length + prepared.Value.Occurrences.Count, firstAffected,
                    prepared.Value.Occurrences.Min(x => x.StartUtc), reason, actorUserId, now),
                now, prepared.Value.Occurrences.Select(x => (x.StartUtc, x.EndUtc)).ToArray(), null, now, token);
        try { await store.SaveAsync(token); }
        catch (TeachingPersistenceException ex) { return Result<ScheduleSeriesReplacedDto>.Failure(PersistenceError(ex)); }
        await guard.CommitAsync(token);
        return Result<ScheduleSeriesReplacedDto>.Success(new(ScheduleSeriesDto.From(series),
            Created(replacement, false), active.Length));
    }

    private async Task<Error> ValidateResourcesAsync(Guid centerId,
        CreateScheduleSeriesRequest request, CancellationToken token)
    {
        var teachingClass = await store.FindClassAsync(centerId, request.ClassId, token);
        if (teachingClass is null || !teachingClass.IsActive) return TeachingErrors.ClassUnavailable;
        var teacher = await store.FindTeacherAsync(centerId, request.TeacherId, token);
        if (teacher is null || !teacher.IsActive) return TeachingErrors.TeacherUnavailable;
        return await store.RoomIsActiveAsync(centerId, request.RoomId, token)
            ? Error.None : TeachingErrors.RoomUnavailable;
    }

    private async Task<IReadOnlyList<ScheduleOccurrenceDto>> FindConflictsAsync(Guid centerId,
        CreateScheduleSeriesRequest request, IReadOnlyList<Occurrence> occurrences, CancellationToken token)
    {
        var existing = await store.ListResourceSessionsAsync(centerId, request.ClassId,
            request.TeacherId, request.RoomId, occurrences.Min(x => x.StartUtc),
            occurrences.Max(x => x.EndUtc), token);
        return occurrences.Select(occurrence =>
        {
            var conflicts = new List<ScheduleConflictDto>();
            foreach (var session in existing.Where(x => x.StartUtc < occurrence.EndUtc &&
                x.EndUtc > occurrence.StartUtc))
            {
                if (session.ClassId == request.ClassId) conflicts.Add(new("Class", session.Id));
                if (session.TeacherId == request.TeacherId) conflicts.Add(new("Teacher", session.Id));
                if (session.RoomId == request.RoomId) conflicts.Add(new("Room", session.Id));
            }
            if (occurrences.Any(other => other.Date != occurrence.Date &&
                other.StartUtc < occurrence.EndUtc && other.EndUtc > occurrence.StartUtc))
                conflicts.Add(new("SameSeries", null));
            return new ScheduleOccurrenceDto(occurrence.Date, occurrence.StartUtc,
                occurrence.EndUtc, conflicts);
        }).ToArray();
    }

    private static Result<Prepared> Prepare(CreateScheduleSeriesRequest request,
        Guid? replacesSeriesId, DateOnly? fromDate)
    {
        if (request is null || request.RequestId == Guid.Empty || request.ClassId == Guid.Empty ||
            request.TeacherId == Guid.Empty || request.RoomId == Guid.Empty ||
            request.StartDate == default || request.EndDate < request.StartDate ||
            request.EndDate.DayNumber - request.StartDate.DayNumber > 179 ||
            request.DurationMinutes is < 15 or > 480 ||
            request.Weekdays is not { Count: >= 1 and <= 7 } ||
            !string.Equals(request.TimeZoneId, CenterTime.ZoneId, StringComparison.OrdinalIgnoreCase))
            return Result<Prepared>.Failure(TeachingErrors.InvalidSeries);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(CenterTime.ZoneId); }
        catch (TimeZoneNotFoundException) { return Result<Prepared>.Failure(TeachingErrors.InvalidSeries); }
        catch (InvalidTimeZoneException) { return Result<Prepared>.Failure(TeachingErrors.InvalidSeries); }
        var days = new HashSet<DayOfWeek>();
        foreach (var name in request.Weekdays)
        {
            if (!Enum.TryParse<DayOfWeek>(name, true, out var day) ||
                !Enum.GetNames<DayOfWeek>().Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                !days.Add(day)) return Result<Prepared>.Failure(TeachingErrors.InvalidSeries);
        }
        var mask = days.Aggregate(0, (current, day) => current | (1 << (int)day));
        var excluded = request.ExcludedDates?.ToArray() ?? [];
        if (excluded.Length > 180 || excluded.Distinct().Count() != excluded.Length ||
            excluded.Any(date => date < request.StartDate || date > request.EndDate ||
                !days.Contains(date.DayOfWeek)))
            return Result<Prepared>.Failure(TeachingErrors.InvalidSeries);
        var excludedSet = excluded.ToHashSet();
        var occurrences = new List<Occurrence>();
        for (var index = 0; index <= request.EndDate.DayNumber - request.StartDate.DayNumber; index++)
        {
            var date = request.StartDate.AddDays(index);
            if (!days.Contains(date.DayOfWeek) || excludedSet.Contains(date)) continue;
            var localStart = DateTime.SpecifyKind(date.ToDateTime(request.LocalStartTime), DateTimeKind.Unspecified);
            DateTime localEnd;
            try { localEnd = localStart.AddMinutes(request.DurationMinutes); }
            catch (ArgumentOutOfRangeException) { return Result<Prepared>.Failure(TeachingErrors.InvalidSeries); }
            if (zone.IsInvalidTime(localStart) || zone.IsInvalidTime(localEnd) ||
                zone.IsAmbiguousTime(localStart) || zone.IsAmbiguousTime(localEnd) ||
                zone.GetUtcOffset(localStart) != zone.GetUtcOffset(localEnd))
                return Result<Prepared>.Failure(TeachingErrors.InvalidLocalTime(date));
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeSpan.Zero);
            var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, zone), TimeSpan.Zero);
            if (end - start < TimeSpan.FromMinutes(15) || end - start > TimeSpan.FromHours(8))
                return Result<Prepared>.Failure(TeachingErrors.InvalidSeries);
            occurrences.Add(new(date, start, end));
        }
        if (occurrences.Count == 0) return Result<Prepared>.Failure(TeachingErrors.NoOccurrences);
        var hashInput = string.Join('|', request.ClassId.ToString("D"), request.TeacherId.ToString("D"),
            request.RoomId.ToString("D"), zone.Id,
            request.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            request.LocalStartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            request.DurationMinutes.ToString(CultureInfo.InvariantCulture),
            mask.ToString(CultureInfo.InvariantCulture),
            string.Join(',', excluded.Order().Select(x => x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))),
            replacesSeriesId?.ToString("D") ?? "",
            fromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        return Result<Prepared>.Success(new(zone, mask, excluded.Order().ToArray(), occurrences, hash));
    }

    private static Error ValidateTiming(Prepared prepared, DateTimeOffset now)
    {
        if (prepared.Occurrences.Any(x => x.StartUtc <= now)) return TeachingErrors.ScheduleNotFuture;
        if (prepared.Occurrences[^1].EndUtc > now.AddYears(1)) return TeachingErrors.ScheduleTooFar;
        return Error.None;
    }

    private Error ValidateCutoff(ScheduleSeries series, DateOnly fromDate)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById(series.TimeZoneId)).Date);
        if (fromDate < today || fromDate < series.StartDate || fromDate > series.EndDate)
            return TeachingErrors.InvalidScheduleCutoff;
        if (series.StoppedFromDate.HasValue && fromDate >= series.StoppedFromDate.Value)
            return TeachingErrors.SeriesAlreadyStopped;
        return Error.None;
    }

    private static ScheduleSeries NewSeries(Guid centerId, CreateScheduleSeriesRequest request,
        Prepared prepared, Guid? replacesSeriesId, int cancelledCount, DateTimeOffset now) => ScheduleSeries.Create(
            centerId, request.RequestId, prepared.RequestHash, request.ClassId,
            request.TeacherId, request.RoomId, prepared.Zone.Id, request.StartDate,
            request.EndDate, request.LocalStartTime, request.DurationMinutes,
            prepared.WeekdaysMask, prepared.ExcludedDates, replacesSeriesId,
            prepared.Occurrences.Count, prepared.Occurrences.Min(x => x.StartUtc),
            prepared.Occurrences.Max(x => x.EndUtc), cancelledCount, now);

    private static TeachingSession[] NewSessions(Guid centerId,
        CreateScheduleSeriesRequest request, Guid seriesId,
        IReadOnlyList<Occurrence> occurrences, DateTimeOffset now) =>
        occurrences.Select(x => TeachingSession.Create(centerId, request.ClassId,
            request.TeacherId, request.RoomId, x.StartUtc, x.EndUtc, now,
            seriesId, x.Date)).ToArray();

    private static ScheduleSeriesCreatedDto Created(ScheduleSeries series, bool replayed) =>
        new(ScheduleSeriesDto.From(series), series.GeneratedSessionCount,
            series.FirstStartUtc, series.LastEndUtc, replayed);

    private static Error PersistenceError(TeachingPersistenceException error) =>
        error.IsScheduleConflict ? TeachingErrors.ScheduleConflict :
        error.IsConcurrency ? TeachingErrors.ConcurrentChange :
        error.ConstraintName == "ux_schedule_series_request" ? TeachingErrors.RequestIdReused :
        TeachingErrors.ConcurrentChange;
}
