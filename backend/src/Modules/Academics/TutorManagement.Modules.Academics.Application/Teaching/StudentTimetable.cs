using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>A session a student is expected at: an active session of a class they were enrolled in when it starts.</summary>
public sealed record BusySlot(Guid SessionId, Guid CenterId, Guid ClassId, Guid StudentId, Guid? UserId,
    DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>A future active session of a class someone is about to join.</summary>
public sealed record CandidateSlot(Guid SessionId, Guid ClassId, DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>Student and login of an enrollment, as seen when a session starts.</summary>
public sealed record RosterStudent(Guid StudentId, Guid? UserId, string FullName, string StudentCode);

public sealed record ClassLabelRow(Guid CenterId, Guid ClassId, string Code, string Name);

/// <summary>Cross-center reads for "is this person busy then?". Lives in Academics because enrollments do.</summary>
public interface IStudentTimetableStore
{
    /// <summary>
    /// Busy slots in [fromUtc, toUtc) of the given student profiles (their own center) and of every profile
    /// linked to the given logins (all centers), within each enrollment's [enrolled, withdrawn) window.
    /// </summary>
    Task<IReadOnlyList<BusySlot>> ListBusyAsync(IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<Guid> userIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token);

    /// <summary>Active sessions of the classes that start in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<CandidateSlot>> ListClassSessionsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken token);

    /// <summary>Students enrolled in the class at the instant (enrolled at or before, not yet withdrawn).</summary>
    Task<IReadOnlyList<RosterStudent>> ListRosterAtAsync(Guid centerId, Guid classId, DateTimeOffset instantUtc,
        CancellationToken token);

    Task<IReadOnlyList<ClassLabelRow>> ReadClassLabelsAsync(IReadOnlyCollection<Guid> classIds, CancellationToken token);

    Task<IReadOnlyDictionary<Guid, string>> ReadCenterNamesAsync(IReadOnlyCollection<Guid> centerIds, CancellationToken token);

    /// <summary>
    /// Active sessions at other centers that overlap [startUtc, endUtc) and are taught by the same login as this
    /// center's teacher profile (0 when the profile has no linked login).
    /// </summary>
    Task<int> CountTeacherSessionsElsewhereAsync(Guid centerId, Guid teacherId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, CancellationToken token);
}

public sealed record ConflictItemDto(DateTimeOffset StartUtc, DateTimeOffset EndUtc, Guid ClassId,
    DateTimeOffset OtherStartUtc, DateTimeOffset OtherEndUtc, bool SameCenter)
{
    /// <summary>Only filled for the same center, or for the student's own view.</summary>
    public string? OtherClassCode { get; init; }
    public string? OtherClassName { get; init; }
    public string? OtherCenterName { get; init; }
    /// <summary>Set when the clash is between two classes picked in the same application.</summary>
    public bool BetweenSelected { get; init; }
}

/// <summary>
/// Decision B5: what joining a class would clash with. A center sees the details of its own classes and only
/// the number of sessions that clash elsewhere; the student sees everything because it is their own schedule.
/// </summary>
public sealed record ScheduleCheckDto(IReadOnlyList<ConflictItemDto> SameCenterConflicts, int OtherCenterConflictCount)
{
    public static readonly ScheduleCheckDto Clear = new([], 0);
    public bool HasSameCenterConflicts => SameCenterConflicts.Count > 0;
}

public sealed record ApplicationClassCheckDto(Guid ClassId, IReadOnlyList<ConflictItemDto> Conflicts);
public sealed record ApplicationCheckDto(IReadOnlyList<ApplicationClassCheckDto> Classes);

/// <summary>What adding or moving one session means for the class's students.</summary>
public sealed record SessionImpactStudentDto(Guid StudentId, string StudentCode, string FullName,
    string OtherClassCode, string OtherClassName, DateTimeOffset OtherStartUtc, DateTimeOffset OtherEndUtc);
public sealed record SessionImpactDto(int AffectedStudentCount, bool IsUrgent,
    IReadOnlyList<SessionImpactStudentDto> SameCenterConflicts, int OtherCenterConflictStudentCount,
    int StudentsWithoutAccount)
{
    /// <summary>
    /// The teacher (by their login) teaches at another center at that time: a count only, never the other center
    /// (cross-center clashes are warnings, the same center is blocked by the schedule rules).
    /// </summary>
    public int TeacherSessionsElsewhere { get; init; }
}

/// <summary>Finds clashes between a class (or one session) and the people who would attend it.</summary>
public sealed class ScheduleConflictChecker(IStudentTimetableStore store, IClock clock)
{
    /// <summary>Candidates are checked this far ahead; a recurring schedule spans at most 180 days.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromDays(200);
    private const int MaxItems = 50;

    /// <summary>Joining <paramref name="classId"/> (manual enrollment, approval, transfer).</summary>
    public async Task<ScheduleCheckDto> CheckJoinAsync(Guid centerId, Guid classId, Guid studentId, Guid? userId,
        CancellationToken token, Guid? ignoreClassId = null)
    {
        var now = clock.UtcNow;
        var candidates = await store.ListClassSessionsAsync(centerId, [classId], now, now + Horizon, token);
        if (candidates.Count == 0) return ScheduleCheckDto.Clear;
        var busy = await store.ListBusyAsync(studentId == Guid.Empty ? [] : [studentId], userId.HasValue ? [userId.Value] : [],
            candidates.Min(x => x.StartUtc), candidates.Max(x => x.EndUtc), token);
        busy = busy.Where(x => x.ClassId != classId && x.ClassId != ignoreClassId).DistinctBy(x => x.SessionId).ToArray();
        var pairs = Overlaps(candidates, busy).ToArray();
        if (pairs.Length == 0) return ScheduleCheckDto.Clear;
        var labels = await LabelsAsync(pairs.Where(x => x.Busy.CenterId == centerId).Select(x => x.Busy.ClassId), token);
        var same = pairs.Where(x => x.Busy.CenterId == centerId).Take(MaxItems).Select(x =>
        {
            var label = labels.GetValueOrDefault(x.Busy.ClassId);
            return new ConflictItemDto(x.Candidate.StartUtc, x.Candidate.EndUtc, classId, x.Busy.StartUtc,
                x.Busy.EndUtc, true) { OtherClassCode = label?.Code, OtherClassName = label?.Name };
        }).ToArray();
        var otherCount = pairs.Where(x => x.Busy.CenterId != centerId).Select(x => x.Candidate.SessionId).Distinct().Count();
        return new ScheduleCheckDto(same, otherCount);
    }

    /// <summary>F3: the student's own view before submitting applications; includes other centers by name.</summary>
    public async Task<Result<ApplicationCheckDto>> CheckApplicationAsync(Guid centerId, Guid userId,
        IReadOnlyList<Guid> classIds, CancellationToken token)
    {
        if (userId == Guid.Empty || classIds is not { Count: >= 1 and <= 10 } || classIds.Any(x => x == Guid.Empty) ||
            classIds.Distinct().Count() != classIds.Count)
            return Result<ApplicationCheckDto>.Failure(ClassRegistrationErrors.InvalidClasses);
        var now = clock.UtcNow;
        var candidates = await store.ListClassSessionsAsync(centerId, classIds, now, now + Horizon, token);
        if (candidates.Count == 0)
            return Result<ApplicationCheckDto>.Success(new(classIds.Select(x => new ApplicationClassCheckDto(x, [])).ToArray()));
        var busy = (await store.ListBusyAsync([], [userId], candidates.Min(x => x.StartUtc),
            candidates.Max(x => x.EndUtc), token)).Where(x => !(x.CenterId == centerId && classIds.Contains(x.ClassId)))
            .DistinctBy(x => x.SessionId).ToArray();
        var pairs = Overlaps(candidates, busy).ToArray();
        var labels = await LabelsAsync(pairs.Select(x => x.Busy.ClassId).Concat(classIds), token);
        var centers = await store.ReadCenterNamesAsync(pairs.Select(x => x.Busy.CenterId).Distinct().ToArray(), token);
        var result = classIds.Select(classId =>
        {
            var own = candidates.Where(x => x.ClassId == classId).ToArray();
            var items = pairs.Where(x => x.Candidate.ClassId == classId).Select(x =>
            {
                var label = labels.GetValueOrDefault(x.Busy.ClassId);
                return new ConflictItemDto(x.Candidate.StartUtc, x.Candidate.EndUtc, classId, x.Busy.StartUtc,
                    x.Busy.EndUtc, x.Busy.CenterId == centerId)
                {
                    OtherClassCode = label?.Code, OtherClassName = label?.Name,
                    OtherCenterName = centers.GetValueOrDefault(x.Busy.CenterId)
                };
            }).ToList();
            // Two classes picked together that meet at the same time.
            foreach (var mine in own)
            foreach (var other in candidates.Where(x => x.ClassId != classId &&
                         x.StartUtc < mine.EndUtc && x.EndUtc > mine.StartUtc))
            {
                var label = labels.GetValueOrDefault(other.ClassId);
                items.Add(new ConflictItemDto(mine.StartUtc, mine.EndUtc, classId, other.StartUtc, other.EndUtc, true)
                {
                    OtherClassCode = label?.Code, OtherClassName = label?.Name, BetweenSelected = true
                });
            }
            return new ApplicationClassCheckDto(classId, items.OrderBy(x => x.StartUtc).Take(MaxItems).ToArray());
        }).ToArray();
        return Result<ApplicationCheckDto>.Success(new(result));
    }

    /// <summary>B5/B6: preview of adding or moving a session of a class (sessionId is excluded when moving it).</summary>
    public async Task<SessionImpactDto> SessionImpactAsync(Guid centerId, Guid classId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, Guid? sessionId, DateTimeOffset? originalStartUtc, CancellationToken token,
        Guid? teacherId = null)
    {
        var students = await StudentImpactAsync(centerId, classId, startUtc, endUtc, sessionId, originalStartUtc, token);
        if (teacherId is not { } teacher || teacher == Guid.Empty) return students;
        return students with
        {
            TeacherSessionsElsewhere = await store.CountTeacherSessionsElsewhereAsync(centerId, teacher, startUtc, endUtc, token)
        };
    }

    private async Task<SessionImpactDto> StudentImpactAsync(Guid centerId, Guid classId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, Guid? sessionId, DateTimeOffset? originalStartUtc, CancellationToken token)
    {
        var now = clock.UtcNow;
        var roster = await store.ListRosterAtAsync(centerId, classId, startUtc > now ? now : startUtc, token);
        var urgent = new[] { (DateTimeOffset?)startUtc, originalStartUtc }.Where(x => x.HasValue)
            .Any(x => x!.Value - now <= ScheduleChange.UrgentWindow);
        if (roster.Count == 0) return new(0, urgent, [], 0, 0);
        var counts = await CountStudentConflictsAsync(centerId, classId, roster, [(startUtc, endUtc)], sessionId, token);
        var labels = await LabelsAsync(counts.SameCenter.Select(x => x.Busy.ClassId), token);
        var same = counts.SameCenter.Take(MaxItems).Select(x =>
        {
            var student = roster.First(r => r.StudentId == x.Student);
            var label = labels.GetValueOrDefault(x.Busy.ClassId);
            return new SessionImpactStudentDto(student.StudentId, student.StudentCode, student.FullName,
                label?.Code ?? "", label?.Name ?? "", x.Busy.StartUtc, x.Busy.EndUtc);
        }).ToArray();
        return new(roster.Count, urgent, same, counts.OtherCenterStudents.Count, roster.Count(x => !x.UserId.HasValue));
    }

    /// <summary>Per student, how many of their other sessions overlap any of the given times.</summary>
    public async Task<IReadOnlyDictionary<Guid, int>> ConflictCountsAsync(Guid centerId, Guid classId,
        IReadOnlyList<RosterStudent> roster, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> times,
        Guid? excludeSessionId, CancellationToken token)
    {
        if (roster.Count == 0 || times.Count == 0) return new Dictionary<Guid, int>();
        var result = await CountStudentConflictsAsync(centerId, classId, roster, times, excludeSessionId, token);
        return result.PerStudent;
    }

    private sealed record StudentClash(Guid Student, BusySlot Busy);
    private sealed record ClashSummary(IReadOnlyList<StudentClash> SameCenter, HashSet<Guid> OtherCenterStudents,
        IReadOnlyDictionary<Guid, int> PerStudent);

    private async Task<ClashSummary> CountStudentConflictsAsync(Guid centerId, Guid classId,
        IReadOnlyList<RosterStudent> roster, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> times,
        Guid? excludeSessionId, CancellationToken token)
    {
        var busy = await store.ListBusyAsync(roster.Select(x => x.StudentId).ToArray(),
            roster.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).Distinct().ToArray(),
            times.Min(x => x.Start), times.Max(x => x.End), token);
        var byUser = roster.Where(x => x.UserId.HasValue).GroupBy(x => x.UserId!.Value)
            .ToDictionary(x => x.Key, x => x.First().StudentId);
        var same = new List<StudentClash>();
        var others = new HashSet<Guid>();
        var perStudent = new Dictionary<Guid, int>();
        var seen = new HashSet<(Guid, Guid)>();
        foreach (var slot in busy.Where(x => x.ClassId != classId && x.SessionId != excludeSessionId))
        {
            var studentId = slot.CenterId == centerId && roster.Any(r => r.StudentId == slot.StudentId)
                ? slot.StudentId
                : slot.UserId is { } user && byUser.TryGetValue(user, out var linked) ? linked : Guid.Empty;
            if (studentId == Guid.Empty) continue;
            if (!times.Any(t => t.Start < slot.EndUtc && t.End > slot.StartUtc)) continue;
            if (!seen.Add((studentId, slot.SessionId))) continue;
            perStudent[studentId] = perStudent.GetValueOrDefault(studentId) + 1;
            if (slot.CenterId == centerId) same.Add(new(studentId, slot));
            else others.Add(studentId);
        }
        return new(same.OrderBy(x => x.Busy.StartUtc).ToArray(), others, perStudent);
    }

    private sealed record Pair(CandidateSlot Candidate, BusySlot Busy);

    private static IEnumerable<Pair> Overlaps(IReadOnlyList<CandidateSlot> candidates, IReadOnlyList<BusySlot> busy) =>
        from candidate in candidates
        from slot in busy
        where slot.SessionId != candidate.SessionId && candidate.StartUtc < slot.EndUtc && candidate.EndUtc > slot.StartUtc
        orderby candidate.StartUtc
        select new Pair(candidate, slot);

    private async Task<Dictionary<Guid, ClassLabelRow>> LabelsAsync(IEnumerable<Guid> classIds, CancellationToken token)
    {
        var ids = classIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await store.ReadClassLabelsAsync(ids, token)).ToDictionary(x => x.ClassId);
    }
}
