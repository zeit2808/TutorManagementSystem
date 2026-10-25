using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

public sealed class TeachingSession : AggregateRoot<Guid>
{
    private TeachingSession() : base(Guid.Empty) { }
    private TeachingSession(Guid centerId, Guid classId, Guid teacherId, Guid roomId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now,
        Guid? seriesId, DateOnly? occurrenceDate) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        TeacherId = teacherId;
        RoomId = roomId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        CreatedAtUtc = now;
        SeriesId = seriesId;
        OccurrenceDate = occurrenceDate;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid TeacherId { get; private set; }
    public Guid RoomId { get; private set; }
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid? SeriesId { get; private set; }
    public DateOnly? OccurrenceDate { get; private set; }
    public DateTimeOffset? RescheduledAtUtc { get; private set; }
    public int Version { get; private set; }
    /// <summary>D7: why the session was cancelled (before it started).</summary>
    public string? CancellationReason { get; private set; }
    /// <summary>D6: the session time passed but the class did not take place.</summary>
    public DateTimeOffset? NotHeldAtUtc { get; private set; }
    public string? NotHeldReason { get; private set; }
    /// <summary>Reason given for the latest reschedule.</summary>
    public string? RescheduleReason { get; private set; }
    public Guid? StatusChangedByUserId { get; private set; }
    /// <summary>D6: this session makes up for a cancelled or not-held session of the same class.</summary>
    public Guid? MakeupForSessionId { get; private set; }
    public bool IsActive => CancelledAtUtc is null && NotHeldAtUtc is null;
    public TeachingSessionStatus Status => NotHeldAtUtc.HasValue ? TeachingSessionStatus.NotHeld
        : CancelledAtUtc.HasValue ? TeachingSessionStatus.Cancelled : TeachingSessionStatus.Scheduled;
    public const int MaxReasonLength = 500;

    public static TeachingSession Create(Guid centerId, Guid classId, Guid teacherId, Guid roomId,
        DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now,
        Guid? seriesId = null, DateOnly? occurrenceDate = null)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty || teacherId == Guid.Empty || roomId == Guid.Empty ||
            !IsValidTime(startUtc, endUtc, now)) throw new ArgumentException("Session details are invalid.");
        if (seriesId.HasValue != occurrenceDate.HasValue)
            throw new ArgumentException("Series and occurrence date must be supplied together.");
        return new(centerId, classId, teacherId, roomId, startUtc, endUtc, now, seriesId, occurrenceDate);
    }

    public void MarkAsMakeupFor(TeachingSession original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.CenterId != CenterId || original.ClassId != ClassId || original.IsActive || original.Id == Id)
            throw new InvalidOperationException("A makeup session replaces a cancelled or not-held session of the same class.");
        MakeupForSessionId = original.Id;
    }

    public void Reschedule(Guid teacherId, Guid roomId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, DateTimeOffset now, string reason, Guid? actorUserId)
    {
        RescheduleReason = RequireReason(reason);
        StatusChangedByUserId = actorUserId;
        Reschedule(teacherId, roomId, startUtc, endUtc, now);
    }

    public void Reschedule(Guid teacherId, Guid roomId, DateTimeOffset startUtc,
        DateTimeOffset endUtc, DateTimeOffset now)
    {
        if (!IsActive || StartUtc <= now || teacherId == Guid.Empty || roomId == Guid.Empty ||
            !IsValidTime(startUtc, endUtc, now))
            throw new InvalidOperationException("Only future active sessions can be rescheduled.");
        TeacherId = teacherId;
        RoomId = roomId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        RescheduledAtUtc = now;
        Version++;
    }

    public void Cancel(DateTimeOffset now) => Cancel(now, SeriesStoppedReason, null);

    public void Cancel(DateTimeOffset now, string reason, Guid? actorUserId)
    {
        if (!IsActive || StartUtc <= now) throw new InvalidOperationException("Only future active sessions can be cancelled.");
        CancellationReason = RequireReason(reason);
        StatusChangedByUserId = actorUserId;
        CancelledAtUtc = now;
        Version++;
    }

    /// <summary>D6: records that a session which has started (or ended) did not take place.</summary>
    public void MarkNotHeld(DateTimeOffset now, string reason, Guid? actorUserId)
    {
        if (!IsActive || StartUtc > now)
            throw new InvalidOperationException("Only an active session that has started can be marked as not held.");
        NotHeldReason = RequireReason(reason);
        StatusChangedByUserId = actorUserId;
        NotHeldAtUtc = now;
        Version++;
    }

    /// <summary>Stored when a whole schedule series is stopped or replaced.</summary>
    public const string SeriesStoppedReason = "Lịch định kỳ thay đổi";

    public static bool IsValidReason(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= MaxReasonLength;

    private static string RequireReason(string reason)
    {
        if (!IsValidReason(reason)) throw new ArgumentException("A reason of at most 500 characters is required.", nameof(reason));
        return reason.Trim();
    }

    public static bool IsValidTime(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now) =>
        startUtc.Offset == TimeSpan.Zero && endUtc.Offset == TimeSpan.Zero && startUtc > now &&
        endUtc > startUtc && endUtc - startUtc >= TimeSpan.FromMinutes(15) &&
        endUtc - startUtc <= TimeSpan.FromHours(8);
}
