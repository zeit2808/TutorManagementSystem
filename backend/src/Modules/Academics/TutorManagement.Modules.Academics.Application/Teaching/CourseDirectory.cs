using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>A center in the public directory. Only the display name is public — no tax code or address.</summary>
public sealed record DirectoryCenterDto(Guid Id, string Name, int OpenClassCount, IReadOnlyList<string> Subjects);

public sealed record WeeklySlotDto(string Weekday, TimeOnly StartTime, int DurationMinutes);

public sealed record DirectoryClassDto(Guid Id, string Name, string SubjectName, IReadOnlyList<string> TeacherNames,
    IReadOnlyList<WeeklySlotDto> WeeklySchedule, DateOnly? StartDate, DateOnly? EndDate, int SeatsLeft);

public sealed record DirectoryCenterDetailDto(Guid Id, string Name, IReadOnlyList<DirectoryClassDto> Classes);

public sealed record DirectoryCenterRow(Guid Id, string Name, int OpenClassCount, string[] Subjects, int TotalCount);

public sealed record DirectoryClassRow(Guid Id, string Name, string SubjectName, int SeatsLeft);

public sealed record DirectorySeriesRow(Guid ClassId, string TeacherName, int WeekdaysMask, TimeOnly LocalStartTime,
    int DurationMinutes);

public sealed record DirectorySessionSpan(Guid ClassId, DateTimeOffset FirstStartUtc, DateTimeOffset LastEndUtc,
    string[] TeacherNames);

public interface ICourseDirectoryStore
{
    Task<IReadOnlyList<DirectoryCenterRow>> ListCentersAsync(string? search, string? subject, int page, int pageSize,
        CancellationToken token);
    Task<IReadOnlyList<string>> ListSubjectsAsync(CancellationToken token);
    Task<string?> FindListedCenterNameAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<DirectoryClassRow>> ListOpenClassesAsync(Guid centerId, string? subject, CancellationToken token);
    Task<IReadOnlyList<DirectorySeriesRow>> ListRunningSeriesAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        DateOnly today, CancellationToken token);
    Task<IReadOnlyList<DirectorySessionSpan>> ListSessionSpansAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        DateTimeOffset nowUtc, CancellationToken token);
}

/// <summary>
/// D4: students browse centers whose owner opted in, and only the classes currently open for registration.
/// Each class shows its subject, weekly schedule, teachers, seats left and start/end dates.
/// </summary>
public sealed class CourseDirectory(ICourseDirectoryStore store, IClock clock)
{
    public static readonly Error CenterNotListed = new("directory_center_not_found",
        "The center is not in the public directory.");

    public async Task<TeachingPage<DirectoryCenterDto>> ListCentersAsync(string? search, string? subject, int page,
        int pageSize, CancellationToken token)
    {
        var rows = await store.ListCentersAsync(Clean(search), Clean(subject), page, pageSize, token);
        return new TeachingPage<DirectoryCenterDto>
        {
            Page = page, PageSize = pageSize, TotalCount = rows.Count == 0 ? 0 : rows[0].TotalCount,
            Items = rows.Select(x => new DirectoryCenterDto(x.Id, x.Name, x.OpenClassCount,
                x.Subjects.Order(StringComparer.CurrentCultureIgnoreCase).ToArray())).ToList()
        };
    }

    public Task<IReadOnlyList<string>> ListSubjectsAsync(CancellationToken token) => store.ListSubjectsAsync(token);

    public async Task<Result<DirectoryCenterDetailDto>> GetCenterAsync(Guid centerId, string? subject,
        CancellationToken token)
    {
        var name = await store.FindListedCenterNameAsync(centerId, token);
        if (name is null) return Result<DirectoryCenterDetailDto>.Failure(CenterNotListed);
        var classes = await store.ListOpenClassesAsync(centerId, Clean(subject), token);
        if (classes.Count == 0) return Result<DirectoryCenterDetailDto>.Success(new(centerId, name, []));

        var ids = classes.Select(x => x.Id).ToArray();
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, CenterTime.Zone).Date);
        var series = (await store.ListRunningSeriesAsync(centerId, ids, today, token)).ToLookup(x => x.ClassId);
        var spans = (await store.ListSessionSpansAsync(centerId, ids, now, token)).ToDictionary(x => x.ClassId);

        var items = classes.Select(item =>
        {
            var running = series[item.Id].ToArray();
            var slots = running.SelectMany(x => Weekdays(x.WeekdaysMask)
                    .Select(day => new WeeklySlotDto(day.ToString(), x.LocalStartTime, x.DurationMinutes)))
                .Distinct()
                .OrderBy(x => DayOrder(x.Weekday)).ThenBy(x => x.StartTime)
                .ToArray();
            spans.TryGetValue(item.Id, out var span);
            var teachers = running.Select(x => x.TeacherName).Concat(span?.TeacherNames ?? [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
            return new DirectoryClassDto(item.Id, item.Name, item.SubjectName, teachers, slots,
                span is null ? null : LocalDate(span.FirstStartUtc),
                span is null ? null : LocalDate(span.LastEndUtc), Math.Max(0, item.SeatsLeft));
        }).ToArray();
        return Result<DirectoryCenterDetailDto>.Success(new(centerId, name, items));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateOnly LocalDate(DateTimeOffset utc) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, CenterTime.Zone).Date);

    private static IEnumerable<DayOfWeek> Weekdays(int mask) =>
        Enumerable.Range(0, 7).Where(x => (mask & (1 << x)) != 0).Select(x => (DayOfWeek)x);

    private static int DayOrder(string weekday) =>
        Enum.TryParse<DayOfWeek>(weekday, out var day) ? (day == DayOfWeek.Sunday ? 7 : (int)day) : 8;
}
