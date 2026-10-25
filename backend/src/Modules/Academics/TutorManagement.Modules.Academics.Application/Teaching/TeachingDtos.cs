using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed class TeachingPage<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = [];
}

public sealed class ClassDto
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int MaxStudents { get; set; }
    public int EnrolledCount { get; set; }
    public bool RegistrationOpen { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public static ClassDto From(TeachingClass value) => new()
    {
        Id = value.Id, SubjectId = value.SubjectId,
        Code = value.Code, Name = value.Name, MaxStudents = value.MaxStudents,
        EnrolledCount = value.EnrolledCount, RegistrationOpen = value.RegistrationOpen,
        IsActive = value.IsActive, Version = value.Version
    };
}

public sealed class StudentDto
{
    public Guid Id { get; set; }
    public bool HasAccount { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? ParentPhone { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    /// <summary>Set when this profile was merged into another one (it is archived).</summary>
    public Guid? MergedIntoStudentId { get; set; }
    /// <summary>M3: a profile of this center that is probably the same student; the center decides.</summary>
    public Guid? DuplicateCandidateId { get; set; }
    public string? DuplicateCandidateCode { get; set; }
    public string? DuplicateCandidateName { get; set; }
    /// <summary>SameName (hint) or SameAccount (proved by an activation link).</summary>
    public string? DuplicateReason { get; set; }
    /// <summary>The latest activation link of a profile without a login.</summary>
    public ActivationStatusDto? Activation { get; set; }
    public static StudentDto From(Student value) => new()
    {
        Id = value.Id, HasAccount = value.UserId.HasValue, Code = value.Code,
        FullName = value.FullName, ParentName = value.ParentName, ParentPhone = value.ParentPhone,
        IsActive = value.IsActive, Version = value.Version, MergedIntoStudentId = value.MergedIntoStudentId,
        DuplicateCandidateId = value.DuplicateCandidateId, DuplicateReason = value.DuplicateReason?.ToString()
    };
}

public sealed class TeacherDto
{
    public Guid Id { get; set; }
    /// <summary>The linked login, when this teacher can sign in to see sessions and take attendance.</summary>
    public Guid? UserId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public static TeacherDto From(Teacher value) => new()
    {
        Id = value.Id, UserId = value.UserId, Code = value.Code,
        FullName = value.FullName, IsActive = value.IsActive, Version = value.Version
    };
}

public sealed class EnrollmentDto
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset EnrolledAtUtc { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public string? WithdrawalReason { get; set; }
    public static EnrollmentDto From(Enrollment value) => new()
    {
        Id = value.Id, StudentId = value.StudentId,
        Status = value.WithdrawnAtUtc.HasValue ? "Withdrawn" : "Active",
        EnrolledAtUtc = value.EnrolledAtUtc, WithdrawnAtUtc = value.WithdrawnAtUtc,
        WithdrawalReason = value.WithdrawalReason
    };
    /// <summary>B5: sessions of the class that clash with another center (count only).</summary>
    public int? OtherCenterConflictCount { get; set; }
}

public enum EnrollmentFilter { Active, Withdrawn, All }

public sealed class ClassStudentDto
{
    public Guid EnrollmentId { get; set; }
    public Guid StudentId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset EnrolledAtUtc { get; set; }
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public string? WithdrawalReason { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public string? WithdrawnByName { get; set; }
}

public sealed class SessionDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public Guid TeacherId { get; set; }
    public Guid RoomId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset EndUtc { get; set; }
    public Guid? SeriesId { get; set; }
    public DateOnly? OccurrenceDate { get; set; }
    public bool IsRescheduled { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public string? ClassCode { get; set; }
    public string? ClassName { get; set; }
    public string? TeacherName { get; set; }
    public string? RoomName { get; set; }
    /// <summary>Scheduled, Cancelled or NotHeld.</summary>
    public string Status { get; set; } = nameof(TeachingSessionStatus.Scheduled);
    public string? CancellationReason { get; set; }
    public string? NotHeldReason { get; set; }
    public string? RescheduleReason { get; set; }
    public DateTimeOffset? StatusChangedAtUtc { get; set; }
    public Guid? MakeupForSessionId { get; set; }
    /// <summary>The time before the latest reschedule (B6), on read endpoints.</summary>
    public DateTimeOffset? PreviousStartUtc { get; set; }
    public DateTimeOffset? PreviousEndUtc { get; set; }
    public static SessionDto From(TeachingSession value) => new()
    {
        Id = value.Id, ClassId = value.ClassId, TeacherId = value.TeacherId,
        RoomId = value.RoomId, StartUtc = value.StartUtc, EndUtc = value.EndUtc,
        SeriesId = value.SeriesId, OccurrenceDate = value.OccurrenceDate,
        IsRescheduled = value.RescheduledAtUtc.HasValue,
        IsActive = value.IsActive, Version = value.Version,
        Status = value.Status.ToString(), CancellationReason = value.CancellationReason,
        NotHeldReason = value.NotHeldReason, RescheduleReason = value.RescheduleReason,
        StatusChangedAtUtc = value.NotHeldAtUtc ?? value.CancelledAtUtc,
        MakeupForSessionId = value.MakeupForSessionId
    };
}

public sealed record ScheduleSeriesDto(Guid Id, Guid ClassId, Guid TeacherId, Guid RoomId,
    string TimeZoneId, DateOnly StartDate, DateOnly EndDate, TimeOnly LocalStartTime,
    int DurationMinutes, IReadOnlyList<string> Weekdays, IReadOnlyList<DateOnly> ExcludedDates,
    int GeneratedSessionCount,
    DateOnly? StoppedFromDate, Guid? ReplacesSeriesId, int Version)
{
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? TeacherName { get; init; }
    public string? RoomName { get; init; }

    public static ScheduleSeriesDto From(ScheduleSeries value) => new(value.Id, value.ClassId,
        value.TeacherId, value.RoomId, value.TimeZoneId, value.StartDate, value.EndDate,
        value.LocalStartTime, value.DurationMinutes,
        Enumerable.Range(0, 7).Select(x => (DayOfWeek)x)
            .Where(x => (value.WeekdaysMask & (1 << (int)x)) != 0)
            .OrderBy(x => x == DayOfWeek.Sunday ? 7 : (int)x)
            .Select(x => x.ToString()).ToArray(),
        value.GetExcludedDates(), value.GeneratedSessionCount, value.StoppedFromDate,
        value.ReplacesSeriesId, value.Version);
}

public sealed record ScheduleConflictDto(string ResourceType, Guid? ExistingSessionId);
public sealed record ScheduleOccurrenceDto(DateOnly OccurrenceDate, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, IReadOnlyList<ScheduleConflictDto> Conflicts);
public sealed record SchedulePreviewDto(int SessionCount, IReadOnlyList<ScheduleOccurrenceDto> Occurrences);
public sealed record ScheduleSeriesCreatedDto(ScheduleSeriesDto Series,
    int SessionsCreated, DateTimeOffset FirstStartUtc, DateTimeOffset LastEndUtc, bool Replayed);
public sealed record ScheduleSeriesStoppedDto(ScheduleSeriesDto Series, int SessionsCancelled);
public sealed record ScheduleSeriesReplacedDto(ScheduleSeriesDto PreviousSeries,
    ScheduleSeriesCreatedDto Replacement, int SessionsCancelled);
