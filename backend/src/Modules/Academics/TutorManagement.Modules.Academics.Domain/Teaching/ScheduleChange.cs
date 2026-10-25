using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

/// <summary>What the center changed. One row per action, so a whole series change is one entry.</summary>
public enum ScheduleChangeKind
{
    SessionAdded = 0,
    SessionRescheduled = 1,
    SessionCancelled = 2,
    SessionNotHeld = 3,
    MakeupScheduled = 4,
    SeriesStopped = 5,
    SeriesReplaced = 6,
    /// <summary>The center enrolled a student who has a login; only that student is told.</summary>
    StudentEnrolled = 7
}

/// <summary>
/// The change log behind "Đã dời từ T3 18:00 → T4 19:00 · lý do": the old and new time, why, who and when.
/// Students are told through <see cref="ScheduleNotice"/> rows created in the same transaction.
/// </summary>
public sealed class ScheduleChange : Entity<Guid>
{
    /// <summary>A single session that starts within this window is flagged as urgent (báo gấp).</summary>
    public static readonly TimeSpan UrgentWindow = TimeSpan.FromHours(24);
    /// <summary>A recurring schedule should be changed at least this long before the first affected session.</summary>
    public static readonly TimeSpan SeriesNoticePeriod = TimeSpan.FromDays(7);

    private ScheduleChange() : base(Guid.Empty) { }

    private ScheduleChange(Guid centerId, Guid classId, ScheduleChangeKind kind, string? reason,
        Guid? actorUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        ClassId = classId;
        Kind = kind;
        Reason = reason;
        ActorUserId = actorUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ClassId { get; private set; }
    public ScheduleChangeKind Kind { get; private set; }
    public Guid? SessionId { get; private set; }
    public Guid? SeriesId { get; private set; }
    public DateTimeOffset? OldStartUtc { get; private set; }
    public DateTimeOffset? OldEndUtc { get; private set; }
    public DateTimeOffset? NewStartUtc { get; private set; }
    public DateTimeOffset? NewEndUtc { get; private set; }
    public Guid? OldTeacherId { get; private set; }
    public Guid? NewTeacherId { get; private set; }
    public Guid? OldRoomId { get; private set; }
    public Guid? NewRoomId { get; private set; }
    public string? Reason { get; private set; }
    public bool IsUrgent { get; private set; }
    /// <summary>Sessions touched by the change (1 for a single session).</summary>
    public int AffectedSessionCount { get; private set; }
    /// <summary>The earliest session the change touches; drives the urgent flag.</summary>
    public DateTimeOffset? FirstAffectedStartUtc { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ScheduleChange ForSession(ScheduleChangeKind kind, TeachingSession session,
        DateTimeOffset? oldStartUtc, DateTimeOffset? oldEndUtc, Guid? oldTeacherId, Guid? oldRoomId,
        string? reason, Guid? actorUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (kind is ScheduleChangeKind.SeriesStopped or ScheduleChangeKind.SeriesReplaced)
            throw new ArgumentException("Use ForSeries for recurring schedule changes.", nameof(kind));
        var change = new ScheduleChange(session.CenterId, session.ClassId, kind, Normalize(reason), actorUserId, now)
        {
            SessionId = session.Id,
            SeriesId = session.SeriesId,
            OldStartUtc = oldStartUtc,
            OldEndUtc = oldEndUtc,
            OldTeacherId = oldTeacherId,
            OldRoomId = oldRoomId,
            AffectedSessionCount = 1
        };
        if (kind is ScheduleChangeKind.SessionAdded or ScheduleChangeKind.SessionRescheduled or ScheduleChangeKind.MakeupScheduled)
        {
            change.NewStartUtc = session.StartUtc;
            change.NewEndUtc = session.EndUtc;
            change.NewTeacherId = session.TeacherId;
            change.NewRoomId = session.RoomId;
        }
        var starts = new[] { oldStartUtc, change.NewStartUtc }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        change.FirstAffectedStartUtc = starts.Length == 0 ? session.StartUtc : starts.Min();
        // A session that already took place (not held) is reported, never "urgent".
        change.IsUrgent = kind != ScheduleChangeKind.SessionNotHeld &&
            change.FirstAffectedStartUtc.Value - now <= UrgentWindow;
        return change;
    }

    public static ScheduleChange ForSeries(ScheduleChangeKind kind, Guid centerId, Guid classId, Guid seriesId,
        int affectedSessionCount, DateTimeOffset? firstAffectedStartUtc, DateTimeOffset? newFirstStartUtc,
        string reason, Guid? actorUserId, DateTimeOffset now)
    {
        if (kind is not (ScheduleChangeKind.SeriesStopped or ScheduleChangeKind.SeriesReplaced))
            throw new ArgumentException("Only series changes are allowed here.", nameof(kind));
        if (!TeachingSession.IsValidReason(reason)) throw new ArgumentException("A reason is required.", nameof(reason));
        var change = new ScheduleChange(centerId, classId, kind, reason.Trim(), actorUserId, now)
        {
            SeriesId = seriesId,
            AffectedSessionCount = affectedSessionCount,
            FirstAffectedStartUtc = firstAffectedStartUtc,
            NewStartUtc = newFirstStartUtc
        };
        change.IsUrgent = IsShortNotice(firstAffectedStartUtc, now);
        return change;
    }

    /// <summary>A notice for one student added to a class by the center (no sessions change).</summary>
    public static ScheduleChange ForEnrollment(Guid centerId, Guid classId, Guid? actorUserId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || classId == Guid.Empty) throw new ArgumentException("Class is required.");
        return new ScheduleChange(centerId, classId, ScheduleChangeKind.StudentEnrolled, null, actorUserId, now);
    }

    /// <summary>The recurring schedule changes less than seven days before its first affected session.</summary>
    public static bool IsShortNotice(DateTimeOffset? firstAffectedStartUtc, DateTimeOffset now) =>
        firstAffectedStartUtc.HasValue && firstAffectedStartUtc.Value - now < SeriesNoticePeriod;

    private static string? Normalize(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}

/// <summary>What a student said after reading a schedule notice.</summary>
public enum NoticeResponse
{
    None = 0,
    /// <summary>Đã xem, vẫn đi được.</summary>
    Acknowledged = 1,
    /// <summary>The student sent a request (absence, makeup, transfer or withdrawal) about this change.</summary>
    Requested = 2
}

/// <summary>One affected student's copy of a <see cref="ScheduleChange"/>: in-app notice and email delivery.</summary>
public sealed class ScheduleNotice : Entity<Guid>
{
    private ScheduleNotice() : base(Guid.Empty) { }

    private ScheduleNotice(ScheduleChange change, Guid studentId, Guid? userId, int conflictCount,
        bool emailQueued, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = change.CenterId;
        ChangeId = change.Id;
        ClassId = change.ClassId;
        StudentId = studentId;
        UserId = userId;
        IsUrgent = change.IsUrgent;
        ConflictCount = conflictCount;
        EmailQueued = emailQueued;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid ChangeId { get; private set; }
    public Guid ClassId { get; private set; }
    public Guid StudentId { get; private set; }
    /// <summary>The student's login when the notice was created; null for students without an account.</summary>
    public Guid? UserId { get; private set; }
    public bool IsUrgent { get; private set; }
    /// <summary>Other sessions of this student (any center) that overlap the new time.</summary>
    public int ConflictCount { get; private set; }
    public bool EmailQueued { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? SeenAtUtc { get; private set; }
    public NoticeResponse Response { get; private set; }
    public DateTimeOffset? RespondedAtUtc { get; private set; }

    public static ScheduleNotice Create(ScheduleChange change, Guid studentId, Guid? userId, int conflictCount,
        bool emailQueued, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (studentId == Guid.Empty || conflictCount < 0) throw new ArgumentException("Notice details are invalid.");
        return new(change, studentId, userId, conflictCount, emailQueued && userId.HasValue, now);
    }

    public bool MarkSeen(DateTimeOffset now)
    {
        if (SeenAtUtc.HasValue) return false;
        SeenAtUtc = now;
        return true;
    }

    public void Acknowledge(DateTimeOffset now)
    {
        MarkSeen(now);
        if (Response == NoticeResponse.Requested) return;
        Response = NoticeResponse.Acknowledged;
        RespondedAtUtc = now;
    }

    public void RecordRequest(DateTimeOffset now)
    {
        MarkSeen(now);
        Response = NoticeResponse.Requested;
        RespondedAtUtc = now;
    }
}
