using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Configurations;

public sealed class TeachingClassConfiguration : IEntityTypeConfiguration<TeachingClass>
{
    public void Configure(EntityTypeBuilder<TeachingClass> builder)
    {
        builder.ToTable("classes", t =>
        {
            t.HasCheckConstraint("ck_classes_max_students", "max_students BETWEEN 1 AND 500");
            t.HasCheckConstraint("ck_classes_enrolled_count", "enrolled_count BETWEEN 0 AND max_students");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.SubjectId).HasColumnName("subject_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.MaxStudents).HasColumnName("max_students");
        builder.Property(x => x.EnrolledCount).HasColumnName("enrolled_count");
        builder.Property(x => x.RegistrationOpen).HasColumnName("registration_open").HasDefaultValue(false);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.IsActive);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<Subject>().WithMany().HasForeignKey(x => new { x.CenterId, x.SubjectId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.Code }).IsUnique().HasDatabaseName("ux_classes_center_code");
    }
}

public sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("students");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParentName).HasColumnName("parent_name").HasMaxLength(100);
        builder.Property(x => x.ParentPhone).HasColumnName("parent_phone").HasMaxLength(16);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.MergedIntoStudentId).HasColumnName("merged_into_student_id");
        builder.Property(x => x.DuplicateCandidateId).HasColumnName("duplicate_candidate_id");
        builder.Property(x => x.DuplicateReason).HasColumnName("duplicate_reason").HasConversion<string>().HasMaxLength(16);
        builder.Ignore(x => x.IsActive);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasIndex(x => new { x.CenterId, x.Code }).IsUnique().HasDatabaseName("ux_students_center_code");
        builder.HasIndex(x => new { x.CenterId, x.UserId }).IsUnique().HasFilter("user_id IS NOT NULL")
            .HasDatabaseName("ux_students_center_user");
    }
}

public sealed class ClassApplicationConfiguration : IEntityTypeConfiguration<ClassApplication>
{
    public void Configure(EntityTypeBuilder<ClassApplication> builder)
    {
        builder.ToTable("class_applications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(80).IsRequired();
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ParentName).HasColumnName("parent_name").HasMaxLength(100);
        builder.Property(x => x.ParentPhone).HasColumnName("parent_phone").HasMaxLength(16);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(500);
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.UserId, x.ClassId }).IsUnique()
            .HasFilter("status IN ('Pending', 'Approved')").HasDatabaseName("ux_class_applications_active_choice");
        builder.HasIndex(x => new { x.CenterId, x.UserId, x.IdempotencyKey, x.ClassId }).IsUnique()
            .HasDatabaseName("ux_class_applications_idempotency_choice");
        builder.HasIndex(x => new { x.CenterId, x.Status, x.SubmittedAtUtc });
        builder.HasIndex(x => new { x.CenterId, x.UserId, x.SubmissionId });
    }
}

public sealed class TeacherConfiguration : IEntityTypeConfiguration<Teacher>
{
    public void Configure(EntityTypeBuilder<Teacher> builder)
    {
        builder.ToTable("teachers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ArchivedAtUtc).HasColumnName("archived_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.IsActive);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasIndex(x => new { x.CenterId, x.Code }).IsUnique().HasDatabaseName("ux_teachers_center_code");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.HasIndex(x => new { x.CenterId, x.UserId }).IsUnique().HasFilter("user_id IS NOT NULL")
            .HasDatabaseName("ux_teachers_center_user");
    }
}

public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
{
    public void Configure(EntityTypeBuilder<Enrollment> builder)
    {
        builder.ToTable("enrollments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.EnrolledAtUtc).HasColumnName("enrolled_at_utc");
        builder.Property(x => x.WithdrawnAtUtc).HasColumnName("withdrawn_at_utc");
        builder.Property(x => x.WithdrawalReason).HasColumnName("withdrawal_reason").HasMaxLength(500);
        builder.Property(x => x.WithdrawnByUserId).HasColumnName("withdrawn_by_user_id");
        builder.Ignore(x => x.IsActive);
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id, x.StudentId });
        builder.HasOne<Student>().WithMany().HasForeignKey(x => new { x.CenterId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ClassId, x.StudentId }).IsUnique()
            .HasFilter("withdrawn_at_utc IS NULL").HasDatabaseName("ux_enrollments_active_class_student");
        builder.HasIndex(x => new { x.CenterId, x.StudentId, x.WithdrawnAtUtc });
    }
}

public sealed class TeachingSessionConfiguration : IEntityTypeConfiguration<TeachingSession>
{
    public void Configure(EntityTypeBuilder<TeachingSession> builder)
    {
        builder.ToTable("teaching_sessions", t =>
        {
            t.HasCheckConstraint("ck_sessions_time", "end_utc > start_utc");
            t.HasCheckConstraint("ck_sessions_series_occurrence",
                "(series_id IS NULL AND occurrence_date IS NULL) OR (series_id IS NOT NULL AND occurrence_date IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.RoomId).HasColumnName("room_id");
        builder.Property(x => x.StartUtc).HasColumnName("start_utc");
        builder.Property(x => x.EndUtc).HasColumnName("end_utc");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at_utc");
        builder.Property(x => x.SeriesId).HasColumnName("series_id");
        builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date");
        builder.Property(x => x.RescheduledAtUtc).HasColumnName("rescheduled_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason")
            .HasMaxLength(TeachingSession.MaxReasonLength);
        builder.Property(x => x.NotHeldAtUtc).HasColumnName("not_held_at_utc");
        builder.Property(x => x.NotHeldReason).HasColumnName("not_held_reason")
            .HasMaxLength(TeachingSession.MaxReasonLength);
        builder.Property(x => x.RescheduleReason).HasColumnName("reschedule_reason")
            .HasMaxLength(TeachingSession.MaxReasonLength);
        builder.Property(x => x.StatusChangedByUserId).HasColumnName("status_changed_by_user_id");
        builder.Property(x => x.MakeupForSessionId).HasColumnName("makeup_for_session_id");
        builder.Ignore(x => x.IsActive);
        builder.Ignore(x => x.Status);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasAlternateKey(x => new { x.CenterId, x.Id, x.ClassId });
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Teacher>().WithMany().HasForeignKey(x => new { x.CenterId, x.TeacherId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScheduleSeries>().WithMany().HasForeignKey(x => new { x.CenterId, x.SeriesId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<TeachingSession>().WithMany().HasForeignKey(x => new { x.CenterId, x.MakeupForSessionId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_sessions_makeup_for");
        // D6: at most one live makeup per missed session.
        builder.HasIndex(x => x.MakeupForSessionId).IsUnique()
            .HasFilter("makeup_for_session_id IS NOT NULL AND cancelled_at_utc IS NULL AND not_held_at_utc IS NULL")
            .HasDatabaseName("ux_sessions_makeup_for");
        builder.HasIndex(x => new { x.CenterId, x.StartUtc });
        builder.HasIndex(x => new { x.SeriesId, x.OccurrenceDate }).IsUnique()
            .HasFilter("series_id IS NOT NULL").HasDatabaseName("ux_sessions_series_occurrence");
    }
}

public sealed class SessionAttendanceConfiguration : IEntityTypeConfiguration<SessionAttendance>
{
    public void Configure(EntityTypeBuilder<SessionAttendance> builder)
    {
        builder.ToTable("attendance_sheets", t => t.HasCheckConstraint("ck_attendance_version", "version > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("session_id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.ActualTeacherId).HasColumnName("actual_teacher_id");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<TeachingSession>().WithOne().HasForeignKey<SessionAttendance>(x =>
            new { x.CenterId, x.Id, x.ClassId }).HasPrincipalKey<TeachingSession>(x => new { x.CenterId, x.Id, x.ClassId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Teacher>().WithMany().HasForeignKey(x => new { x.CenterId, x.ActualTeacherId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.ClassId, x.Status });
    }
}

public sealed class AttendanceEntryConfiguration : IEntityTypeConfiguration<AttendanceEntry>
{
    public void Configure(EntityTypeBuilder<AttendanceEntry> builder)
    {
        builder.ToTable("attendance_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.EnrollmentId).HasColumnName("enrollment_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.Mark).HasColumnName("mark").HasConversion<string>().HasMaxLength(24);
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
        builder.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.HasOne<SessionAttendance>().WithMany().HasForeignKey(x => new { x.CenterId, x.SessionId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Enrollment>().WithMany().HasForeignKey(x => new { x.CenterId, x.EnrollmentId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id, x.StudentId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Student>().WithMany().HasForeignKey(x => new { x.CenterId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.SessionId, x.StudentId }).IsUnique()
            .HasDatabaseName("ux_attendance_student_session");
    }
}

public sealed class AttendanceRevisionConfiguration : IEntityTypeConfiguration<AttendanceRevision>
{
    public void Configure(EntityTypeBuilder<AttendanceRevision> builder)
    {
        builder.ToTable("attendance_revisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.Version).HasColumnName("version");
        builder.Property(x => x.Action).HasColumnName("action").HasMaxLength(24);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(x => x.BeforeJson).HasColumnName("before_json").HasColumnType("jsonb");
        builder.Property(x => x.AfterJson).HasColumnName("after_json").HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasOne<SessionAttendance>().WithMany().HasForeignKey(x => new { x.CenterId, x.SessionId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.SessionId, x.Version }).IsUnique()
            .HasDatabaseName("ux_attendance_revision_version");
    }
}

public sealed class ScheduleSeriesConfiguration : IEntityTypeConfiguration<ScheduleSeries>
{
    public void Configure(EntityTypeBuilder<ScheduleSeries> builder)
    {
        builder.ToTable("schedule_series", t =>
        {
            t.HasCheckConstraint("ck_series_dates", "end_date >= start_date AND end_date - start_date <= 179");
            t.HasCheckConstraint("ck_series_duration", "duration_minutes BETWEEN 15 AND 480");
            t.HasCheckConstraint("ck_series_weekdays", "weekdays_mask BETWEEN 1 AND 127");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.TeacherId).HasColumnName("teacher_id");
        builder.Property(x => x.RoomId).HasColumnName("room_id");
        builder.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.StartDate).HasColumnName("start_date");
        builder.Property(x => x.EndDate).HasColumnName("end_date");
        builder.Property(x => x.LocalStartTime).HasColumnName("local_start_time");
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(x => x.WeekdaysMask).HasColumnName("weekdays_mask");
        builder.Property(x => x.ExcludedDates).HasColumnName("excluded_dates").HasMaxLength(2048).IsRequired();
        builder.Property(x => x.ReplacesSeriesId).HasColumnName("replaces_series_id");
        builder.Property(x => x.GeneratedSessionCount).HasColumnName("generated_session_count");
        builder.Property(x => x.FirstStartUtc).HasColumnName("first_start_utc");
        builder.Property(x => x.LastEndUtc).HasColumnName("last_end_utc");
        builder.Property(x => x.ReplacedSessionsCancelled).HasColumnName("replaced_sessions_cancelled");
        builder.Property(x => x.StoppedFromDate).HasColumnName("stopped_from_date");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Teacher>().WithMany().HasForeignKey(x => new { x.CenterId, x.TeacherId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ScheduleSeries>().WithMany().HasForeignKey(x => new { x.CenterId, x.ReplacesSeriesId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CenterId, x.ClassId, x.StartDate });
        builder.HasIndex(x => new { x.CenterId, x.RequestId }).IsUnique()
            .HasDatabaseName("ux_schedule_series_request");
    }
}

public sealed class ScheduleChangeConfiguration : IEntityTypeConfiguration<ScheduleChange>
{
    public void Configure(EntityTypeBuilder<ScheduleChange> builder)
    {
        builder.ToTable("schedule_changes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.SeriesId).HasColumnName("series_id");
        builder.Property(x => x.OldStartUtc).HasColumnName("old_start_utc");
        builder.Property(x => x.OldEndUtc).HasColumnName("old_end_utc");
        builder.Property(x => x.NewStartUtc).HasColumnName("new_start_utc");
        builder.Property(x => x.NewEndUtc).HasColumnName("new_end_utc");
        builder.Property(x => x.OldTeacherId).HasColumnName("old_teacher_id");
        builder.Property(x => x.NewTeacherId).HasColumnName("new_teacher_id");
        builder.Property(x => x.OldRoomId).HasColumnName("old_room_id");
        builder.Property(x => x.NewRoomId).HasColumnName("new_room_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(TeachingSession.MaxReasonLength);
        builder.Property(x => x.IsUrgent).HasColumnName("is_urgent");
        builder.Property(x => x.AffectedSessionCount).HasColumnName("affected_session_count");
        builder.Property(x => x.FirstAffectedStartUtc).HasColumnName("first_affected_start_utc");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_schedule_changes_class");
        builder.HasOne<TeachingSession>().WithMany().HasForeignKey(x => new { x.CenterId, x.SessionId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_schedule_changes_session");
        builder.HasIndex(x => new { x.CenterId, x.CreatedAtUtc }).HasDatabaseName("ix_schedule_changes_center_created");
        builder.HasIndex(x => new { x.SessionId, x.CreatedAtUtc }).HasDatabaseName("ix_schedule_changes_session");
    }
}

public sealed class ScheduleNoticeConfiguration : IEntityTypeConfiguration<ScheduleNotice>
{
    public void Configure(EntityTypeBuilder<ScheduleNotice> builder)
    {
        builder.ToTable("schedule_notices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ChangeId).HasColumnName("change_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.IsUrgent).HasColumnName("is_urgent");
        builder.Property(x => x.ConflictCount).HasColumnName("conflict_count");
        builder.Property(x => x.EmailQueued).HasColumnName("email_queued");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.SeenAtUtc).HasColumnName("seen_at_utc");
        builder.Property(x => x.Response).HasColumnName("response").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.RespondedAtUtc).HasColumnName("responded_at_utc");
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<ScheduleChange>().WithMany().HasForeignKey(x => new { x.CenterId, x.ChangeId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_schedule_notices_change");
        builder.HasOne<Student>().WithMany().HasForeignKey(x => new { x.CenterId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_schedule_notices_student");
        builder.HasIndex(x => new { x.ChangeId, x.StudentId }).IsUnique().HasDatabaseName("ux_schedule_notices_change_student");
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).HasDatabaseName("ix_schedule_notices_user");
    }
}

public sealed class StudentRequestConfiguration : IEntityTypeConfiguration<StudentRequest>
{
    public void Configure(EntityTypeBuilder<StudentRequest> builder)
    {
        builder.ToTable("student_requests", t =>
        {
            t.HasCheckConstraint("ck_student_requests_session",
                "(kind IN ('ExcusedAbsence','Makeup')) = (session_id IS NOT NULL)");
            t.HasCheckConstraint("ck_student_requests_target",
                "(kind = 'Transfer') = (target_class_id IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id");
        builder.Property(x => x.TargetClassId).HasColumnName("target_class_id");
        builder.Property(x => x.NoticeId).HasColumnName("notice_id");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(StudentRequest.MaxReasonLength).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.SubmittedAtUtc).HasColumnName("submitted_at_utc");
        builder.Property(x => x.ReviewedAtUtc).HasColumnName("reviewed_at_utc");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.ReviewNote).HasColumnName("review_note").HasMaxLength(StudentRequest.MaxReasonLength);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Ignore(x => x.NeedsSession);
        builder.HasAlternateKey(x => new { x.CenterId, x.Id });
        builder.HasOne<Student>().WithMany().HasForeignKey(x => new { x.CenterId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_requests_student");
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_requests_class");
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.TargetClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_requests_target_class");
        builder.HasOne<TeachingSession>().WithMany().HasForeignKey(x => new { x.CenterId, x.SessionId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_requests_session");
        builder.HasOne<ScheduleNotice>().WithMany().HasForeignKey(x => new { x.CenterId, x.NoticeId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_requests_notice");
        // One open request of a kind per session (absence, makeup) or per class (transfer, withdrawal).
        builder.HasIndex(x => new { x.StudentId, x.Kind, x.SessionId }).IsUnique()
            .HasFilter("status = 'Pending' AND session_id IS NOT NULL").HasDatabaseName("ux_student_requests_pending_session");
        builder.HasIndex(x => new { x.StudentId, x.Kind, x.ClassId }).IsUnique()
            .HasFilter("status = 'Pending' AND session_id IS NULL").HasDatabaseName("ux_student_requests_pending_class");
        builder.HasIndex(x => new { x.CenterId, x.Status, x.SubmittedAtUtc }).HasDatabaseName("ix_student_requests_center_status");
        builder.HasIndex(x => new { x.UserId, x.SubmittedAtUtc }).HasDatabaseName("ix_student_requests_user");
    }
}

public sealed class StudentActivationLinkConfiguration : IEntityTypeConfiguration<StudentActivationLink>
{
    public void Configure(EntityTypeBuilder<StudentActivationLink> builder)
    {
        builder.ToTable("student_activation_links");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.UsedAtUtc).HasColumnName("used_at_utc");
        builder.Property(x => x.UsedByUserId).HasColumnName("used_by_user_id");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(x => x.DeclinedAtUtc).HasColumnName("declined_at_utc");
        builder.HasOne<Student>().WithMany().HasForeignKey(x => new { x.CenterId, x.StudentId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_student_activation_links_student");
        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_student_activation_links_token");
        builder.HasIndex(x => new { x.CenterId, x.StudentId, x.CreatedAtUtc })
            .HasDatabaseName("ix_student_activation_links_student");
    }
}

public sealed class ClassInviteLinkConfiguration : IEntityTypeConfiguration<ClassInviteLink>
{
    public void Configure(EntityTypeBuilder<ClassInviteLink> builder)
    {
        builder.ToTable("class_invite_links", t =>
            t.HasCheckConstraint("ck_class_invite_links_uses", "use_count >= 0 AND (max_uses IS NULL OR max_uses > 0)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.Token).HasColumnName("token").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(ClassInviteLink.CodeLength).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
        builder.Property(x => x.MaxUses).HasColumnName("max_uses");
        builder.Property(x => x.UseCount).HasColumnName("use_count");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasOne<TeachingClass>().WithMany().HasForeignKey(x => new { x.CenterId, x.ClassId })
            .HasPrincipalKey(x => new { x.CenterId, x.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_class_invite_links_class");
        builder.HasIndex(x => x.Token).IsUnique().HasDatabaseName("ux_class_invite_links_token");
        builder.HasIndex(x => new { x.CenterId, x.ClassId, x.CreatedAtUtc }).HasDatabaseName("ix_class_invite_links_class");
    }
}

public sealed class ClassInviteAttemptConfiguration : IEntityTypeConfiguration<ClassInviteAttempt>
{
    public void Configure(EntityTypeBuilder<ClassInviteAttempt> builder)
    {
        builder.ToTable("class_invite_attempts");
        builder.HasKey(x => new { x.LinkId, x.UserId });
        builder.Property(x => x.LinkId).HasColumnName("link_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.FailedCount).HasColumnName("failed_count");
        builder.Property(x => x.LockedUntilUtc).HasColumnName("locked_until_utc");
        builder.HasOne<ClassInviteLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_class_invite_attempts_link");
    }
}

public sealed class ClassInviteJoinConfiguration : IEntityTypeConfiguration<ClassInviteJoin>
{
    public void Configure(EntityTypeBuilder<ClassInviteJoin> builder)
    {
        builder.ToTable("class_invite_joins");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CenterId).HasColumnName("center_id");
        builder.Property(x => x.LinkId).HasColumnName("link_id");
        builder.Property(x => x.ClassId).HasColumnName("class_id");
        builder.Property(x => x.StudentId).HasColumnName("student_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.CreatedProfile).HasColumnName("created_profile");
        builder.Property(x => x.HadConflicts).HasColumnName("had_conflicts");
        builder.Property(x => x.JoinedAtUtc).HasColumnName("joined_at_utc");
        builder.HasOne<ClassInviteLink>().WithMany().HasForeignKey(x => x.LinkId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_class_invite_joins_link");
        builder.HasIndex(x => new { x.CenterId, x.JoinedAtUtc }).HasDatabaseName("ix_class_invite_joins_center");
    }
}
