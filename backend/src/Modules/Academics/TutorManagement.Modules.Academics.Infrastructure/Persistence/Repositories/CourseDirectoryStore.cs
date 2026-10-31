using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

/// <summary>Read-only queries for the public directory (D4). Centers live in the organization schema.</summary>
public sealed class CourseDirectoryStore(AcademicsDbContext db) : ICourseDirectoryStore
{
    private const string OpenClasses = """
        SELECT c.center_id, c.id, c.name AS class_name, s.name AS subject_name
        FROM academics.classes c
        JOIN academics.subjects s ON s.center_id = c.center_id AND s.id = c.subject_id
        WHERE c.archived_at_utc IS NULL AND c.registration_open AND c.enrolled_count < c.max_students
        """;

    private const string ListedCenter = "ce.status = 'Approved' AND ce.listed_in_directory";

    public async Task<IReadOnlyList<DirectoryCenterRow>> ListCentersAsync(string? search, string? subject, int page,
        int pageSize, CancellationToken token)
    {
        var sql = $"""
            WITH oc AS ({OpenClasses})
            SELECT ce.id AS "Id", ce.display_name AS "Name",
                   COUNT(oc.id)::int AS "OpenClassCount",
                   COALESCE(ARRAY_AGG(DISTINCT oc.subject_name) FILTER (WHERE oc.subject_name IS NOT NULL), ARRAY[]::text[]) AS "Subjects",
                   (COUNT(*) OVER ())::int AS "TotalCount"
            FROM organization.centers ce
            LEFT JOIN oc ON oc.center_id = ce.id
            WHERE {ListedCenter}
              AND (@search::text IS NULL OR ce.display_name ILIKE @search ESCAPE '\'
                   OR EXISTS (SELECT 1 FROM oc o2 WHERE o2.center_id = ce.id AND o2.class_name ILIKE @search ESCAPE '\'))
              AND (@subject::text IS NULL OR EXISTS (SELECT 1 FROM oc o3 WHERE o3.center_id = ce.id
                   AND o3.subject_name ILIKE @subject ESCAPE '\'))
            GROUP BY ce.id, ce.display_name
            ORDER BY COUNT(oc.id) > 0 DESC, ce.display_name, ce.id
            LIMIT @take OFFSET @skip
            """;
        return await db.Database.SqlQueryRaw<DirectoryCenterRow>(sql,
                Text("search", Pattern(search)), Text("subject", Pattern(subject)),
                new NpgsqlParameter("take", pageSize), new NpgsqlParameter("skip", (page - 1) * pageSize))
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<string>> ListSubjectsAsync(CancellationToken token) =>
        await db.Database.SqlQueryRaw<string>($"""
            WITH oc AS ({OpenClasses})
            SELECT DISTINCT oc.subject_name AS "Value"
            FROM oc JOIN organization.centers ce ON ce.id = oc.center_id
            WHERE {ListedCenter}
            ORDER BY 1
            LIMIT 200
            """).ToListAsync(token);

    public async Task<string?> FindListedCenterNameAsync(Guid centerId, CancellationToken token) =>
        await db.Database.SqlQueryRaw<string>($"""
            SELECT ce.display_name AS "Value" FROM organization.centers ce WHERE ce.id = @center AND {ListedCenter}
            """, new NpgsqlParameter("center", centerId)).SingleOrDefaultAsync(token);

    public async Task<IReadOnlyList<DirectoryClassRow>> ListOpenClassesAsync(Guid centerId, string? subject,
        CancellationToken token) =>
        await db.Database.SqlQueryRaw<DirectoryClassRow>("""
            SELECT c.id AS "Id", c.name AS "Name", s.name AS "SubjectName",
                   (c.max_students - c.enrolled_count) AS "SeatsLeft"
            FROM academics.classes c
            JOIN academics.subjects s ON s.center_id = c.center_id AND s.id = c.subject_id
            WHERE c.center_id = @center AND c.archived_at_utc IS NULL AND c.registration_open
              AND c.enrolled_count < c.max_students
              AND (@subject::text IS NULL OR s.name ILIKE @subject ESCAPE '\')
            ORDER BY s.name, c.name, c.id
            LIMIT 200
            """, new NpgsqlParameter("center", centerId), Text("subject", Pattern(subject))).ToListAsync(token);

    public async Task<IReadOnlyList<DirectorySeriesRow>> ListRunningSeriesAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, DateOnly today, CancellationToken token) =>
        await (from series in db.ScheduleSeries.AsNoTracking()
               join teacher in db.Teachers.AsNoTracking()
                   on new { series.CenterId, Id = series.TeacherId } equals new { teacher.CenterId, teacher.Id }
               where series.CenterId == centerId && classIds.Contains(series.ClassId) && series.EndDate >= today &&
                     (series.StoppedFromDate == null || series.StoppedFromDate > today)
               select new DirectorySeriesRow(series.ClassId, teacher.FullName, series.WeekdaysMask,
                   series.LocalStartTime, series.DurationMinutes)).ToListAsync(token);

    public async Task<IReadOnlyList<DirectorySessionSpan>> ListSessionSpansAsync(Guid centerId,
        IReadOnlyCollection<Guid> classIds, DateTimeOffset nowUtc, CancellationToken token)
    {
        var spans = await db.Sessions.AsNoTracking()
            .Where(x => x.CenterId == centerId && classIds.Contains(x.ClassId) && x.CancelledAtUtc == null &&
                x.NotHeldAtUtc == null)
            .GroupBy(x => x.ClassId)
            .Select(g => new { ClassId = g.Key, First = g.Min(x => x.StartUtc), Last = g.Max(x => x.EndUtc) })
            .ToListAsync(token);
        // Teachers of upcoming sessions, so substitutes on one-off sessions are named too.
        var teachers = await (from session in db.Sessions.AsNoTracking()
                              join teacher in db.Teachers.AsNoTracking()
                                  on new { session.CenterId, Id = session.TeacherId } equals new { teacher.CenterId, teacher.Id }
                              where session.CenterId == centerId && classIds.Contains(session.ClassId) &&
                                    session.CancelledAtUtc == null && session.NotHeldAtUtc == null &&
                                    session.EndUtc > nowUtc
                              select new { session.ClassId, teacher.FullName }).Distinct().ToListAsync(token);
        var names = teachers.ToLookup(x => x.ClassId, x => x.FullName);
        return spans.Select(x => new DirectorySessionSpan(x.ClassId, x.First, x.Last, names[x.ClassId].ToArray()))
            .ToArray();
    }

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static string? Pattern(string? value) => value is null ? null
        : "%" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
