using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Finance.Application.Payroll;

namespace TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

/// <summary>Read-only SQL over Academics / Organizations for payroll, on the finance connection (inside its transaction).</summary>
public sealed class PayrollAcademicsReader(FinanceDbContext db) : IPayrollAcademicsReader
{
    public async Task<IReadOnlyList<PayrollTeacherRow>> ListTeachersAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<TeacherRow>("""
            SELECT id AS "Id", code AS "Code", full_name AS "FullName", user_id AS "UserId",
                   archived_at_utc IS NOT NULL AS "IsArchived"
            FROM academics.teachers WHERE center_id = @center
            """, new NpgsqlParameter("center", centerId)).ToListAsync(token))
        .Select(x => new PayrollTeacherRow(x.Id, x.Code, x.FullName, x.UserId, x.IsArchived)).ToArray();

    public async Task<IReadOnlyList<PayrollClassRow>> ListClassesAsync(Guid centerId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<ClassRow>("""
            SELECT id AS "Id", code AS "Code", name AS "Name" FROM academics.classes WHERE center_id = @center
            """, new NpgsqlParameter("center", centerId)).ToListAsync(token))
        .Select(x => new PayrollClassRow(x.Id, x.Code, x.Name)).ToArray();

    /// <summary>C5: a sheet counts once submitted; its actual teacher is who taught.</summary>
    public async Task<IReadOnlyList<PayrollSessionRow>> ListSessionsAsync(Guid centerId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<SessionRow>("""
            SELECT s.id AS "Id", s.class_id AS "ClassId", s.teacher_id AS "TeacherId", s.start_utc AS "StartUtc",
                   s.end_utc AS "EndUtc", (s.cancelled_at_utc IS NULL AND s.not_held_at_utc IS NULL) AS "IsActive",
                   (a.status = 'Submitted') AS "Submitted", a.actual_teacher_id AS "ActualTeacherId",
                   s.makeup_for_session_id AS "MakeupForSessionId"
            FROM academics.teaching_sessions s
            LEFT JOIN academics.attendance_sheets a ON a.session_id = s.id AND a.center_id = s.center_id
            WHERE s.center_id = @center AND s.start_utc >= @from AND s.start_utc < @to
            ORDER BY s.start_utc
            """, new NpgsqlParameter("center", centerId), new NpgsqlParameter("from", fromUtc.UtcDateTime),
            new NpgsqlParameter("to", toUtc.UtcDateTime)).ToListAsync(token))
        .Select(x => new PayrollSessionRow(x.Id, x.ClassId, x.TeacherId, x.StartUtc, x.EndUtc, x.IsActive,
            x.Submitted == true, x.ActualTeacherId, x.MakeupForSessionId)).ToArray();

    public async Task<IReadOnlyList<PayrollTeacherProfile>> ListTeacherProfilesOfUserAsync(Guid userId, CancellationToken token) =>
        (await db.Database.SqlQueryRaw<ProfileRow>("""
            SELECT t.center_id AS "CenterId", t.id AS "TeacherId"
            FROM academics.teachers t JOIN organization.centers c ON c.id = t.center_id
            WHERE t.user_id = @user AND c.status = 'Approved'
            """, new NpgsqlParameter("user", userId)).ToListAsync(token))
        .Select(x => new PayrollTeacherProfile(x.CenterId, x.TeacherId)).ToArray();

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

    public Task<bool> CenterExistsAsync(Guid centerId, CancellationToken token) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS(SELECT 1 FROM organization.centers WHERE id = @center) AS \"Value\"",
            new NpgsqlParameter("center", centerId)).SingleAsync(token);

    public async Task<IReadOnlyList<PayrollCenterRow>> ListOperatingCentersAsync(CancellationToken token) =>
        await db.Database.SqlQueryRaw<PayrollCenterRow>("""
            SELECT id AS "Id", display_name AS "DisplayName", status AS "Status" FROM organization.centers
            WHERE status IN ('Approved', 'Suspended') ORDER BY display_name
            """).ToListAsync(token);

    public sealed record TeacherRow(Guid Id, string Code, string FullName, Guid? UserId, bool IsArchived);
    public sealed record ClassRow(Guid Id, string Code, string Name);
    public sealed record SessionRow(Guid Id, Guid ClassId, Guid TeacherId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
        bool IsActive, bool? Submitted, Guid? ActualTeacherId, Guid? MakeupForSessionId);
    public sealed record ProfileRow(Guid CenterId, Guid TeacherId);
}
