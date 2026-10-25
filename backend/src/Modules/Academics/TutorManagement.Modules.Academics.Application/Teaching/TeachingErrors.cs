using TutorManagement.SharedKernel.Primitives;
using System.Globalization;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public static class TeachingErrors
{
    public static readonly Error Invalid = new("invalid_teaching_details", "Teaching details are invalid.");
    public static readonly Error NotFound = new("teaching_resource_not_found", "The resource was not found in this center.");
    public static readonly Error SubjectUnavailable = new("subject_unavailable", "An active subject in this center is required.", "subjectId");
    public static readonly Error RoomUnavailable = new("room_unavailable", "An active room in this center is required.", "roomId");
    public static readonly Error TeacherUnavailable = new("teacher_unavailable", "An active teacher in this center is required.", "teacherId");
    public static readonly Error StudentUnavailable = new("student_unavailable", "An active student in this center is required.", "studentId");
    public static readonly Error ClassUnavailable = new("class_unavailable", "An active class in this center is required.", "classId");
    public static readonly Error CodeExists = new("teaching_code_exists", "This code is already used in this center.", "code");
    public static readonly Error Full = new("class_full", "The class has reached its student limit.", "maxStudents");
    public static readonly Error AlreadyEnrolled = new("student_already_enrolled", "The student is already enrolled in this class.");
    public static readonly Error OnlineRegistrationRequired = new("online_registration_required",
        "Linked student accounts must submit a class application for center review.");
    public static readonly Error NoActiveEnrollment = new("enrollment_not_found", "No active enrollment was found.");
    public static readonly Error HasEnrollments = new("class_has_enrollments", "Withdraw students before archiving this class.");
    public static readonly Error StudentHasEnrollments = new("student_has_enrollments", "Withdraw this student from classes before archiving.");
    public static readonly Error HasFutureSessions = new("future_sessions_exist", "Cancel future sessions before archiving this resource.");
    public static readonly Error ScheduleConflict = new("schedule_conflict", "The class, teacher or room is busy at this time.");
    public static readonly Error InvalidSeries = new("invalid_schedule_series", "The recurring schedule request is invalid.");
    public static readonly Error ScheduleNotFuture = new("schedule_not_future", "Every generated session must start in the future.", "startDate");
    public static readonly Error ScheduleTooFar = new("schedule_too_far", "The last session must end within one year.", "endDate");
    public static readonly Error InvalidScheduleCutoff = new("invalid_schedule_cutoff", "fromDate must be a future date within the original series.", "fromDate");
    public static Error InvalidLocalTime(DateOnly date) => new("invalid_schedule_local_time",
        $"Local time on {date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is missing, ambiguous or crosses a time-zone offset change.",
        "localStartTime");
    public static readonly Error NoOccurrences = new("no_schedule_occurrences", "The selected weekdays and exclusions produce no sessions.", "weekdays");
    public static readonly Error SeriesAlreadyStopped = new("schedule_series_stopped", "This part of the schedule has already been stopped.");
    public static readonly Error StartedSession = new("session_already_started", "A selected session has already started and cannot be changed.");
    public static readonly Error AttendanceExists = new("attendance_exists", "This session already has attendance and cannot be rescheduled or cancelled.");
    public static readonly Error InvalidAttendance = new("invalid_attendance", "Attendance details are invalid.");
    public static readonly Error InvalidAttendanceRoster = new("invalid_attendance_roster", "Attendance must reference students enrolled when the session began.");
    public static readonly Error CancelledAttendanceSession = new("attendance_session_cancelled", "A cancelled session cannot be marked.");
    public static readonly Error AttendanceNotStarted = new("attendance_session_not_started", "Attendance can be saved after the session starts.");
    public static readonly Error AttendanceSubmitted = new("attendance_submitted", "Submitted attendance must be changed through a reasoned correction.");
    public static readonly Error AttendanceDraftRequired = new("attendance_draft_required", "The required attendance draft or submitted sheet does not exist.");
    public static readonly Error AttendanceIncomplete = new("attendance_incomplete", "Select an actual teacher and mark every student before submitting.");
    public static readonly Error AttendanceUnchanged = new("attendance_unchanged", "The correction does not change the attendance sheet.");
    public static readonly Error RequestIdReused = new("schedule_request_id_reused", "This requestId was already used for another schedule.", "requestId");
    public static readonly Error ConcurrentChange = new("teaching_concurrent_change", "The record changed. Reload and retry.");
    public static readonly Error AlreadyActive = new("teaching_already_active", "This resource is already active.");
    public static readonly Error InvalidParentName = new("invalid_parent_name",
        "Parent name must contain 2–100 characters.", "parentName");
    public static readonly Error InvalidParentPhone = new("invalid_parent_phone",
        "Parent phone must be a Vietnamese mobile number.", "parentPhone");
    public static readonly Error InvalidWithdrawalReason = new("invalid_withdrawal_reason",
        "A withdrawal reason of 1–500 characters is required.", "reason");
    public static readonly Error AttendanceNotAssigned = new("attendance_not_assigned",
        "Teachers can only open attendance for sessions they are assigned to or recorded as teaching.");
    public static readonly Error AttendanceWindowClosed = new("attendance_window_closed",
        "Teachers can save attendance until the end of the session day; ask the center to correct it now.");
    public static readonly Error AccountNotMember = new("teacher_account_not_member",
        "The account must be an active member of this center.", "userId");
    public static readonly Error AccountInUse = new("teacher_account_in_use",
        "This account is already linked to another teacher profile in this center.", "userId");
    public static readonly Error InvalidSessionReason = new("invalid_session_reason",
        "A reason of 1–500 characters is required.", "reason");
    public static readonly Error SessionNotStarted = new("session_not_started",
        "Only a session that has started can be marked as not held; cancel future sessions instead.");
    public static readonly Error InvalidMakeup = new("invalid_makeup_session",
        "A makeup session must replace a cancelled or not-held session of the same class.", "makeupForSessionId");
    public static readonly Error StudentScheduleConflict = new("student_schedule_conflict",
        "The student has other classes in this center at the same time. Confirm with acknowledgeConflicts to continue.",
        "acknowledgeConflicts");
    public static readonly Error MakeupExists = new("makeup_session_exists",
        "This session already has an active makeup session.", "makeupForSessionId");
}

public sealed class TeachingPersistenceException(string constraintName, bool concurrency, Exception inner,
    bool scheduleConflict = false)
    : Exception("A teaching write failed.", inner)
{
    public string ConstraintName { get; } = constraintName;
    public bool IsConcurrency { get; } = concurrency;
    public bool IsScheduleConflict { get; } = scheduleConflict;
}
