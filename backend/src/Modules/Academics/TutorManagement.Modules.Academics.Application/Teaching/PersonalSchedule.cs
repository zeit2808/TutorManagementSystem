using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>A session in one person's schedule, either as a student of the class or as its assigned teacher.</summary>
public sealed record PersonalSessionRow(TeachingSession Session, string Role);

/// <summary>Display data for personal schedule rows, gathered across centers in one round trip each.</summary>
public sealed record PersonalScheduleLookups(
    IReadOnlyDictionary<Guid, string> Centers,
    IReadOnlyDictionary<Guid, ClassLabel> Classes,
    IReadOnlyDictionary<Guid, string> Teachers,
    IReadOnlyDictionary<Guid, string> Rooms,
    IReadOnlyDictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)> PreviousTimes,
    IReadOnlyDictionary<Guid, StudentRequestStatus> AbsenceRequests);

public interface IPersonalScheduleStore
{
    /// <summary>
    /// Sessions of every center for the login: as a student, the sessions of their classes whose start falls in
    /// each enrollment's [enrolled, withdrawn) window (any status); as a teacher, sessions assigned to a teacher
    /// profile linked to the login.
    /// </summary>
    Task<IReadOnlyList<PersonalSessionRow>> ListAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken token);

    Task<PersonalScheduleLookups> ReadLookupsAsync(Guid userId, IReadOnlyList<TeachingSession> sessions,
        CancellationToken token);
}

public sealed record MyScheduleEntryDto(Guid SessionId, Guid CenterId, string Role, Guid ClassId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, string Status)
{
    public string? CenterName { get; init; }
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? TeacherName { get; init; }
    public string? RoomName { get; init; }
    /// <summary>Why the session was cancelled or did not take place.</summary>
    public string? StatusReason { get; init; }
    public bool IsRescheduled { get; init; }
    public string? RescheduleReason { get; init; }
    /// <summary>The time before the latest reschedule: "Đã dời từ … → …".</summary>
    public DateTimeOffset? PreviousStartUtc { get; init; }
    public DateTimeOffset? PreviousEndUtc { get; init; }
    public bool IsMakeup { get; init; }
    /// <summary>Other active entries of this person that overlap this one (any center).</summary>
    public IReadOnlyList<Guid> OverlapsWith { get; init; } = [];
    /// <summary>Pending or Approved when the student asked to be excused from this session.</summary>
    public string? AbsenceRequestStatus { get; init; }
}

public sealed record MyScheduleCenterDto(Guid Id, string Name);
public sealed record MyScheduleDto(IReadOnlyList<MyScheduleEntryDto> Entries, IReadOnlyList<MyScheduleCenterDto> Centers);

/// <summary>
/// B4: the combined schedule of one login across every center, computed from enrollments and assignments
/// (never stored per person). Only the person themselves can read it.
/// </summary>
public sealed class PersonalScheduleWorkflow(IPersonalScheduleStore store)
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(42);

    public async Task<Result<MyScheduleDto>> GetAsync(Guid userId, DateTimeOffset fromUtc, DateTimeOffset toUtc,
        CancellationToken token)
    {
        if (userId == Guid.Empty || fromUtc == default || toUtc <= fromUtc || toUtc - fromUtc > MaxRange)
            return Result<MyScheduleDto>.Failure(ClassRegistrationErrors.InvalidSessionRange with
            {
                Message = "Provide an increasing date range of at most 42 days."
            });
        fromUtc = fromUtc.ToUniversalTime();
        toUtc = toUtc.ToUniversalTime();
        var rows = await store.ListAsync(userId, fromUtc, toUtc, token);
        // The same session can appear twice for an owner who teaches it and is also enrolled (rare): keep one.
        rows = rows.GroupBy(x => x.Session.Id).Select(x => x.OrderBy(r => r.Role == "Teacher" ? 0 : 1).First())
            .OrderBy(x => x.Session.StartUtc).ThenBy(x => x.Session.Id).ToArray();
        if (rows.Count == 0) return Result<MyScheduleDto>.Success(new([], []));
        var lookups = await store.ReadLookupsAsync(userId, rows.Select(x => x.Session).ToArray(), token);
        var active = rows.Where(x => x.Session.IsActive).Select(x => x.Session).ToArray();
        var entries = rows.Select(row =>
        {
            var s = row.Session;
            var label = lookups.Classes.GetValueOrDefault(s.ClassId);
            var previous = lookups.PreviousTimes.TryGetValue(s.Id, out var times) ? times : ((DateTimeOffset, DateTimeOffset)?)null;
            return new MyScheduleEntryDto(s.Id, s.CenterId, row.Role, s.ClassId, s.StartUtc, s.EndUtc, s.Status.ToString())
            {
                CenterName = lookups.Centers.GetValueOrDefault(s.CenterId),
                ClassCode = label?.Code,
                ClassName = label?.Name,
                TeacherName = lookups.Teachers.GetValueOrDefault(s.TeacherId),
                RoomName = lookups.Rooms.GetValueOrDefault(s.RoomId),
                StatusReason = s.NotHeldReason ?? s.CancellationReason,
                IsRescheduled = s.RescheduledAtUtc.HasValue,
                RescheduleReason = s.RescheduleReason,
                PreviousStartUtc = previous?.Item1,
                PreviousEndUtc = previous?.Item2,
                IsMakeup = s.MakeupForSessionId.HasValue,
                OverlapsWith = s.IsActive
                    ? active.Where(x => x.Id != s.Id && x.StartUtc < s.EndUtc && x.EndUtc > s.StartUtc).Select(x => x.Id).ToArray()
                    : [],
                AbsenceRequestStatus = row.Role == "Student" && lookups.AbsenceRequests.TryGetValue(s.Id, out var status)
                    ? status.ToString() : null
            };
        }).ToArray();
        var centers = entries.Select(x => x.CenterId).Distinct()
            .Select(id => new MyScheduleCenterDto(id, lookups.Centers.GetValueOrDefault(id) ?? "Trung tâm"))
            .OrderBy(x => x.Name, StringComparer.CurrentCulture).ToArray();
        return Result<MyScheduleDto>.Success(new(entries, centers));
    }
}
