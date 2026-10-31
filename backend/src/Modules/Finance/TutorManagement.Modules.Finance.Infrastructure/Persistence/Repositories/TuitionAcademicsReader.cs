using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Finance.Application.Tuition;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>
/// Read-only SQL over the Academics and Organizations schemas, on the finance connection so it sees the same
/// transaction (the same way Academics reads <c>iam</c> and <c>organization</c>).
/// </summary>
public sealed class TuitionAcademicsReader(FinanceDbContext db) : ITuitionAcademicsReader
{
    public async Task<IReadOnlyList<TuitionClassRow>> ListClassesAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<ClassRow>("""
            SELECT id AS "Id", code AS "Code", name AS "Name", archived_at_utc IS NOT NULL AS "IsArchived"
            FROM academics.classes WHERE center_id = @center
            """, new NpgsqlParameter("center", centerId)).ToListAsync(token))
        .Select(x => new TuitionClassRow(x.Id, x.Code, x.Name, x.IsArchived)).ToArray();

    public async Task<IReadOnlyList<TuitionSessionRow>> ListSessionsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token) =>
        (await db.Database.SqlQueryRaw<SessionRow>("""
            SELECT class_id AS "ClassId", start_utc AS "StartUtc" FROM academics.teaching_sessions
            WHERE center_id = @center AND class_id = ANY(@classes) AND cancelled_at_utc IS NULL AND not_held_at_utc IS NULL
            ORDER BY start_utc
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("classes", classIds.ToArray()))
            .ToListAsync(token))
        .Select(x => new TuitionSessionRow(x.ClassId, x.StartUtc)).ToArray();

    public async Task<IReadOnlyList<TuitionMissedSessionRow>> ListMissedSessionsAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<MissedRow>("""
            SELECT s.class_id AS "ClassId", s.id AS "SessionId", s.start_utc AS "StartUtc",
                   CASE WHEN s.cancelled_at_utc IS NOT NULL THEN 'Cancelled' ELSE 'NotHeld' END AS "Kind",
                   COALESCE(s.cancellation_reason, s.not_held_reason) AS "Reason",
                   EXISTS (SELECT 1 FROM academics.teaching_sessions m
                           WHERE m.center_id = s.center_id AND m.makeup_for_session_id = s.id
                             AND m.cancelled_at_utc IS NULL) AS "HasMakeup"
            FROM academics.teaching_sessions s
            WHERE s.center_id = @center AND s.class_id = ANY(@classes)
              AND (s.cancelled_at_utc IS NOT NULL OR s.not_held_at_utc IS NOT NULL)
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("classes", classIds.ToArray()))
            .ToListAsync(token))
        .Select(x => new TuitionMissedSessionRow(x.ClassId, x.SessionId, x.StartUtc, x.Kind, x.Reason, x.HasMakeup)).ToArray();

    public async Task<IReadOnlyList<TuitionAbsenceRow>> ListAbsencesAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token) =>
        (await db.Database.SqlQueryRaw<AbsenceRow>("""
            SELECT s.class_id AS "ClassId", s.id AS "SessionId", s.start_utc AS "StartUtc",
                   e.enrollment_id AS "EnrollmentId", e.mark AS "Mark"
            FROM academics.attendance_entries e
            JOIN academics.attendance_sheets a ON a.session_id = e.session_id AND a.center_id = e.center_id
            JOIN academics.teaching_sessions s ON s.id = e.session_id AND s.center_id = e.center_id
            WHERE e.center_id = @center AND s.class_id = ANY(@classes) AND a.status = 'Submitted'
              AND e.mark IN ('ExcusedAbsent', 'UnexcusedAbsent')
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("classes", classIds.ToArray()))
            .ToListAsync(token))
        .Select(x => new TuitionAbsenceRow(x.ClassId, x.SessionId, x.StartUtc, x.EnrollmentId, x.Mark)).ToArray();

    public async Task<IReadOnlyList<TuitionEnrollmentRow>> ListEnrollmentsAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<EnrollmentRow>("""
            SELECT id AS "Id", class_id AS "ClassId", student_id AS "StudentId", enrolled_at_utc AS "EnrolledAtUtc",
                   withdrawn_at_utc AS "WithdrawnAtUtc"
            FROM academics.enrollments WHERE center_id = @center AND class_id = ANY(@classes)
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("classes", classIds.ToArray()))
            .ToListAsync(token))
        .Select(x => new TuitionEnrollmentRow(x.Id, x.ClassId, x.StudentId, x.EnrolledAtUtc, x.WithdrawnAtUtc)).ToArray();

    public async Task<IReadOnlyList<TuitionStudentRow>> ListStudentsAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<StudentRow>("""
            SELECT id AS "Id", code AS "Code", full_name AS "FullName", user_id AS "UserId",
                   merged_into_student_id AS "MergedIntoStudentId", parent_name AS "ParentName",
                   parent_phone AS "ParentPhone", archived_at_utc IS NOT NULL AS "IsArchived"
            FROM academics.students WHERE center_id = @center
            """, new NpgsqlParameter("center", centerId)).ToListAsync(token))
        .Select(x => new TuitionStudentRow(x.Id, x.Code, x.FullName, x.UserId, x.MergedIntoStudentId, x.ParentName,
            x.ParentPhone, x.IsArchived)).ToArray();

    public async Task<IReadOnlyList<TuitionStudentLink>> ListProfilesOfUserAsync(Guid userId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<LinkRow>("""
            SELECT s.center_id AS "CenterId", s.id AS "StudentId"
            FROM academics.students s JOIN organization.centers c ON c.id = s.center_id
            WHERE s.user_id = @user AND s.merged_into_student_id IS NULL AND c.status = 'Approved'
            """, new NpgsqlParameter("user", userId)).ToListAsync(token))
        .Select(x => new TuitionStudentLink(x.CenterId, x.StudentId)).ToArray();

    public async Task<IReadOnlyDictionary<Guid, string>> ReadCenterNamesAsync(IReadOnlyCollection<Guid> centerIds,
        CancellationToken token)
    {
        var ids = centerIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        var rows = await db.Database.SqlQueryRaw<TuitionStore.IdText>(
            "SELECT id AS \"Id\", display_name AS \"Text\" FROM organization.centers WHERE id = ANY(@ids)",
            new NpgsqlParameter("ids", ids)).ToListAsync(token);
        return rows.ToDictionary(x => x.Id, x => x.Text);
    }

    public Task<bool> ClassExistsAsync(Guid centerId, Guid classId, CancellationToken token) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS(SELECT 1 FROM academics.classes WHERE center_id = @center AND id = @class) AS \"Value\"",
            new NpgsqlParameter("center", centerId), new NpgsqlParameter("class", classId)).SingleAsync(token);

    public sealed record ClassRow(Guid Id, string Code, string Name, bool IsArchived);
    public sealed record SessionRow(Guid ClassId, DateTimeOffset StartUtc);
    public sealed record MissedRow(Guid ClassId, Guid SessionId, DateTimeOffset StartUtc, string Kind, string? Reason, bool HasMakeup);
    public sealed record AbsenceRow(Guid ClassId, Guid SessionId, DateTimeOffset StartUtc, Guid EnrollmentId, string Mark);
    public sealed record EnrollmentRow(Guid Id, Guid ClassId, Guid StudentId, DateTimeOffset EnrolledAtUtc, DateTimeOffset? WithdrawnAtUtc);
    public sealed record StudentRow(Guid Id, string Code, string FullName, Guid? UserId, Guid? MergedIntoStudentId,
        string? ParentName, string? ParentPhone, bool IsArchived);
    public sealed record LinkRow(Guid CenterId, Guid StudentId);
}
