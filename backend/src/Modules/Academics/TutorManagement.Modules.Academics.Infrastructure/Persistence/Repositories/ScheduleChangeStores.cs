using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class ScheduleChangeStore(AcademicsDbContext db, IClock clock, IServiceProvider services) : IScheduleChangeStore
{
    public string? StudentPortalUrl { get; } = Normalize(services.GetService<IConfiguration>()?["Frontend:BaseUrl"]);

    public async Task AddAsync(ScheduleChange change, IReadOnlyList<ScheduleNotice> notices, CancellationToken token)
    {
        await db.ScheduleChanges.AddAsync(change, token);
        await db.ScheduleNotices.AddRangeAsync(notices, token);
    }

    /// <summary>
    /// The outbox table belongs to IdentityAccess; writing it on this connection keeps the email inside the
    /// schedule change transaction, so a rolled-back change sends nothing.
    /// </summary>
    public async Task QueueEmailsAsync(IReadOnlyList<OutgoingEmail> emails, CancellationToken token)
    {
        var now = clock.UtcNow;
        foreach (var email in emails)
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO iam.email_outbox_messages (id, recipient, subject, body, created_at_utc, attempt_count)
                VALUES ({id}, {email.Recipient}, {email.Subject}, {email.Body}, {now}, 0)
                """, token);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ReadEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token)
    {
        var ids = userIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, string>();
        var rows = await db.Database.SqlQueryRaw<StudentTimetableStore.NameRow>(
                "SELECT id AS \"Id\", email AS \"Name\" FROM iam.users WHERE id = ANY(@ids) AND is_active",
                new NpgsqlParameter("ids", ids)).ToListAsync(token);
        return rows.ToDictionary(x => x.Id, x => x.Name);
    }

    public async Task<IReadOnlyList<ScheduleChange>> ListSessionChangesAsync(Guid centerId, Guid sessionId, CancellationToken token) =>
        await db.ScheduleChanges.AsNoTracking().Where(x => x.CenterId == centerId && x.SessionId == sessionId)
            .OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(token);

    public async Task<(IReadOnlyList<ScheduleChange> Items, int TotalCount)> ListCenterChangesAsync(Guid centerId,
        Guid? classId, int page, int pageSize, CancellationToken token)
    {
        var query = db.ScheduleChanges.AsNoTracking().Where(x => x.CenterId == centerId &&
            x.Kind != ScheduleChangeKind.StudentEnrolled && (classId == null || x.ClassId == classId));
        var total = await query.CountAsync(token);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, NoticeResponseCounts>> CountResponsesAsync(Guid centerId,
        IReadOnlyCollection<Guid> changeIds, CancellationToken token)
    {
        var ids = changeIds.ToArray();
        var notices = await db.ScheduleNotices.AsNoTracking()
            .Where(x => x.CenterId == centerId && ids.Contains(x.ChangeId))
            .Select(x => new { x.Id, x.ChangeId, x.UserId, x.SeenAtUtc, x.Response }).ToListAsync(token);
        var noticeIds = notices.Select(x => x.Id).ToArray();
        var requests = await db.StudentRequests.AsNoTracking()
            .Where(x => x.CenterId == centerId && x.NoticeId != null && noticeIds.Contains(x.NoticeId.Value) &&
                x.Status != StudentRequestStatus.Cancelled)
            .Select(x => new { NoticeId = x.NoticeId!.Value, x.Kind, x.Status }).ToListAsync(token);
        var changeOfNotice = notices.ToDictionary(x => x.Id, x => x.ChangeId);
        return ids.ToDictionary(id => id, id =>
        {
            var own = notices.Where(x => x.ChangeId == id).ToArray();
            var asked = requests.Where(x => changeOfNotice[x.NoticeId] == id).ToArray();
            return new NoticeResponseCounts(own.Length, own.Count(x => x.UserId == null), own.Count(x => x.SeenAtUtc != null),
                own.Count(x => x.Response == NoticeResponse.Acknowledged),
                asked.Count(x => x.Status == StudentRequestStatus.Pending),
                asked.GroupBy(x => x.Kind.ToString()).ToDictionary(x => x.Key, x => x.Count()));
        });
    }

    public async Task<(IReadOnlyList<MyNoticeRow> Items, int TotalCount, int UnreadCount)> ListMyNoticesAsync(Guid userId,
        bool unreadOnly, int page, int pageSize, CancellationToken token)
    {
        var query = from notice in db.ScheduleNotices.AsNoTracking()
                    join change in db.ScheduleChanges.AsNoTracking() on notice.ChangeId equals change.Id
                    where notice.UserId == userId && (!unreadOnly || notice.SeenAtUtc == null)
                    select new { notice, change };
        var total = await query.CountAsync(token);
        var unread = await db.ScheduleNotices.CountAsync(x => x.UserId == userId && x.SeenAtUtc == null, token);
        var rows = await query.OrderByDescending(x => x.notice.CreatedAtUtc).ThenBy(x => x.notice.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token);
        return (rows.Select(x => new MyNoticeRow(x.notice, x.change)).ToArray(), total, unread);
    }

    public Task<ScheduleNotice?> FindMyNoticeAsync(Guid userId, Guid noticeId, CancellationToken token) =>
        db.ScheduleNotices.SingleOrDefaultAsync(x => x.Id == noticeId && x.UserId == userId, token);

    public Task<int> MarkAllSeenAsync(Guid userId, DateTimeOffset now, CancellationToken token) =>
        db.ScheduleNotices.Where(x => x.UserId == userId && x.SeenAtUtc == null)
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.SeenAtUtc, now), token);

    public Task SaveAsync(CancellationToken token) => db.SaveChangesAsync(token);

    private static string? Normalize(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/student"
            : null;
}

public sealed class StudentRequestStore(AcademicsDbContext db) : IStudentRequestStore
{
    public Task<Student?> FindStudentForUserAsync(Guid centerId, Guid userId, CancellationToken token) =>
        db.Students.SingleOrDefaultAsync(x => x.CenterId == centerId && x.UserId == userId, token);

    public Task<StudentRequest?> FindAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.StudentRequests.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);

    public Task<StudentRequest?> FindSnapshotAsync(Guid centerId, Guid id, CancellationToken token) =>
        db.StudentRequests.AsNoTracking().SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == id, token);

    public Task<bool> HasOpenAsync(Guid studentId, StudentRequestKind kind, Guid classId, Guid? sessionId,
        CancellationToken token) =>
        db.StudentRequests.AnyAsync(x => x.StudentId == studentId && x.Kind == kind &&
            (sessionId == null ? x.ClassId == classId && x.SessionId == null : x.SessionId == sessionId) &&
            (x.Status == StudentRequestStatus.Pending ||
             (kind == StudentRequestKind.ExcusedAbsence && x.Status == StudentRequestStatus.Approved)), token);

    public Task<ScheduleNotice?> FindNoticeAsync(Guid centerId, Guid noticeId, Guid studentId, CancellationToken token) =>
        db.ScheduleNotices.SingleOrDefaultAsync(x => x.CenterId == centerId && x.Id == noticeId && x.StudentId == studentId, token);

    public async Task<(IReadOnlyList<StudentRequest> Items, int TotalCount)> ListCenterAsync(Guid centerId,
        StudentRequestStatus? status, StudentRequestKind? kind, Guid? classId, int page, int pageSize, CancellationToken token)
    {
        var query = db.StudentRequests.AsNoTracking().Where(x => x.CenterId == centerId &&
            (status == null || x.Status == status) && (kind == null || x.Kind == kind) &&
            (classId == null || x.ClassId == classId || x.TargetClassId == classId));
        var total = await query.CountAsync(token);
        // Pending first-come first-served; handled ones newest first.
        var ordered = status == StudentRequestStatus.Pending
            ? query.OrderBy(x => x.SubmittedAtUtc)
            : query.OrderByDescending(x => x.SubmittedAtUtc);
        return (await ordered.ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token), total);
    }

    public async Task<(IReadOnlyList<StudentRequest> Items, int TotalCount)> ListMineAsync(Guid userId, int page,
        int pageSize, CancellationToken token)
    {
        var query = db.StudentRequests.AsNoTracking().Where(x => x.UserId == userId);
        var total = await query.CountAsync(token);
        return (await query.OrderByDescending(x => x.SubmittedAtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(token), total);
    }

    public async Task<IReadOnlyDictionary<Guid, (string Code, string Name)>> ReadStudentsAsync(
        IReadOnlyCollection<Guid> studentIds, CancellationToken token)
    {
        var ids = studentIds.ToArray();
        return (await db.Students.AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.FullName }).ToListAsync(token))
            .ToDictionary(x => x.Id, x => (x.Code, x.FullName));
    }

    public async Task<IReadOnlyDictionary<Guid, (DateTimeOffset Start, DateTimeOffset End)>> ReadSessionTimesAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken token)
    {
        var ids = sessionIds.ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, (DateTimeOffset, DateTimeOffset)>();
        return (await db.Sessions.AsNoTracking().Where(x => ids.Contains(x.Id))
                .Select(x => new { x.Id, x.StartUtc, x.EndUtc }).ToListAsync(token))
            .ToDictionary(x => x.Id, x => (x.StartUtc, x.EndUtc));
    }

    public Task<bool> SheetSubmittedAsync(Guid centerId, Guid sessionId, CancellationToken token) =>
        db.AttendanceSheets.AnyAsync(x => x.CenterId == centerId && x.Id == sessionId &&
            x.Status == AttendanceSheetStatus.Submitted, token);

    public async Task AddAsync(StudentRequest request, CancellationToken token) =>
        await db.StudentRequests.AddAsync(request, token);
}
