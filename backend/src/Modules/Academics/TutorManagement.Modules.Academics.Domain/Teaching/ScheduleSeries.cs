using System.Globalization;
using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public sealed class ScheduleSeries : AggregateRoot<Guid>
{
    private ScheduleSeries() : base(Guid.Empty) { }

    private ScheduleSeries(Guid centerId, Guid requestId, string requestHash,
        Guid classId, Guid teacherId, Guid roomId,
        string timeZoneId, DateOnly startDate, DateOnly endDate, TimeOnly localStartTime,
        int durationMinutes, int weekdaysMask, IReadOnlyList<DateOnly> excludedDates,
        Guid? replacesSeriesId, int generatedSessionCount, DateTimeOffset firstStartUtc,
        DateTimeOffset lastEndUtc, int replacedSessionsCancelled, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        RequestId = requestId;
        RequestHash = requestHash;
        ClassId = classId;
        TeacherId = teacherId;
        RoomId = roomId;
        TimeZoneId = timeZoneId;
        StartDate = startDate;
        EndDate = endDate;
        LocalStartTime = localStartTime;
        DurationMinutes = durationMinutes;
        WeekdaysMask = weekdaysMask;
        ExcludedDates = string.Join(',', excludedDates.Select(x => x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        ReplacesSeriesId = replacesSeriesId;
        GeneratedSessionCount = generatedSessionCount;
        FirstStartUtc = firstStartUtc;
        LastEndUtc = lastEndUtc;
        ReplacedSessionsCancelled = replacedSessionsCancelled;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid RequestId { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public Guid ClassId { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid RoomId { get; private set; }
    public string TimeZoneId { get; private set; } = string.Empty;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public TimeOnly LocalStartTime { get; private set; }
    public int DurationMinutes { get; private set; }
    public int WeekdaysMask { get; private set; }
    public string ExcludedDates { get; private set; } = string.Empty;
    public Guid? ReplacesSeriesId { get; private set; }
    public int GeneratedSessionCount { get; private set; }
    public DateTimeOffset FirstStartUtc { get; private set; }
    public DateTimeOffset LastEndUtc { get; private set; }
    public int ReplacedSessionsCancelled { get; private set; }
    public DateOnly? StoppedFromDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static ScheduleSeries Create(Guid centerId, Guid requestId, string requestHash,
        Guid classId, Guid teacherId, Guid roomId,
        string timeZoneId, DateOnly startDate, DateOnly endDate, TimeOnly localStartTime,
        int durationMinutes, int weekdaysMask, IReadOnlyList<DateOnly> excludedDates,
        Guid? replacesSeriesId, int generatedSessionCount, DateTimeOffset firstStartUtc,
        DateTimeOffset lastEndUtc, int replacedSessionsCancelled, DateTimeOffset now) =>
        new(centerId, requestId, requestHash, classId, teacherId, roomId, timeZoneId, startDate, endDate,
            localStartTime, durationMinutes, weekdaysMask, excludedDates, replacesSeriesId,
            generatedSessionCount, firstStartUtc, lastEndUtc, replacedSessionsCancelled, now);

    public void StopFrom(DateOnly date)
    {
        if (date < StartDate || date > EndDate || (StoppedFromDate.HasValue && date >= StoppedFromDate.Value))
            throw new InvalidOperationException("The series cannot be stopped from this date.");
        StoppedFromDate = date;
        Version++;
    }

    public IReadOnlyList<DateOnly> GetExcludedDates() => string.IsNullOrEmpty(ExcludedDates)
        ? []
        : ExcludedDates.Split(',').Select(x => DateOnly.ParseExact(x, "yyyy-MM-dd",
            CultureInfo.InvariantCulture)).ToArray();
}
