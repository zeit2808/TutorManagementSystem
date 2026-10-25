using System.Globalization;
using System.Text;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record OutgoingEmail(string Recipient, string Subject, string Body);

public sealed record NoticeResponseCounts(int Notified, int WithoutAccount, int Seen, int Acknowledged,
    int PendingRequests, IReadOnlyDictionary<string, int> RequestsByKind);

public sealed record MyNoticeRow(ScheduleNotice Notice, ScheduleChange Change);

public interface IScheduleChangeStore
{
    /// <summary>The student portal address used in emails, or null when the front-end URL is not configured.</summary>
    string? StudentPortalUrl { get; }
    Task AddAsync(ScheduleChange change, IReadOnlyList<ScheduleNotice> notices, CancellationToken token);
    /// <summary>Queues emails in the shared outbox inside the current transaction.</summary>
    Task QueueEmailsAsync(IReadOnlyList<OutgoingEmail> emails, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, string>> ReadEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token);
    Task<IReadOnlyList<ScheduleChange>> ListSessionChangesAsync(Guid centerId, Guid sessionId, CancellationToken token);
    Task<(IReadOnlyList<ScheduleChange> Items, int TotalCount)> ListCenterChangesAsync(Guid centerId, Guid? classId,
        int page, int pageSize, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, NoticeResponseCounts>> CountResponsesAsync(Guid centerId,
        IReadOnlyCollection<Guid> changeIds, CancellationToken token);
    Task<(IReadOnlyList<MyNoticeRow> Items, int TotalCount, int UnreadCount)> ListMyNoticesAsync(Guid userId,
        bool unreadOnly, int page, int pageSize, CancellationToken token);
    Task<ScheduleNotice?> FindMyNoticeAsync(Guid userId, Guid noticeId, CancellationToken token);
    Task<int> MarkAllSeenAsync(Guid userId, DateTimeOffset now, CancellationToken token);
    Task SaveAsync(CancellationToken token);
}

public sealed record ScheduleChangeDto(Guid Id, string Kind, Guid ClassId, Guid? SessionId, Guid? SeriesId,
    DateTimeOffset? OldStartUtc, DateTimeOffset? OldEndUtc, DateTimeOffset? NewStartUtc, DateTimeOffset? NewEndUtc,
    string? Reason, bool IsUrgent, int AffectedSessionCount, DateTimeOffset? FirstAffectedStartUtc,
    DateTimeOffset CreatedAtUtc)
{
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
    public string? OldTeacherName { get; init; }
    public string? NewTeacherName { get; init; }
    public string? OldRoomName { get; init; }
    public string? NewRoomName { get; init; }
    public string? ActorName { get; init; }
    /// <summary>F6: how students answered ("15 đã xem · 2 xin vắng · 1 xin học bù").</summary>
    public NoticeResponseCounts? Responses { get; init; }

    public static ScheduleChangeDto From(ScheduleChange x) => new(x.Id, x.Kind.ToString(), x.ClassId, x.SessionId,
        x.SeriesId, x.OldStartUtc, x.OldEndUtc, x.NewStartUtc, x.NewEndUtc, x.Reason, x.IsUrgent,
        x.AffectedSessionCount, x.FirstAffectedStartUtc, x.CreatedAtUtc);
}

public sealed record MyNoticeDto(Guid Id, Guid CenterId, Guid ClassId, string Kind, Guid? SessionId,
    DateTimeOffset? OldStartUtc, DateTimeOffset? OldEndUtc, DateTimeOffset? NewStartUtc, DateTimeOffset? NewEndUtc,
    string? Reason, bool IsUrgent, int AffectedSessionCount, DateTimeOffset? FirstAffectedStartUtc, int ConflictCount,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? SeenAtUtc, string Response)
{
    public string? CenterName { get; init; }
    public string? ClassCode { get; init; }
    public string? ClassName { get; init; }
}

public sealed record MyNoticePage(int Page, int PageSize, int TotalCount, int UnreadCount, IReadOnlyList<MyNoticeDto> Items);

public static class ScheduleChangeErrors
{
    public static readonly Error ShortNotice = new("schedule_short_notice",
        "The first affected session is less than 7 days away. Confirm with acknowledgeShortNotice to change it anyway.",
        "acknowledgeShortNotice");
    public static readonly Error NoticeNotFound = new("schedule_notice_not_found", "The notice was not found.");
}

/// <summary>
/// B6/B7: writes the change log row and one notice per affected student, and queues an email for students who
/// have an account — all inside the caller's transaction, so a failed change never notifies anyone.
/// </summary>
public sealed class ScheduleChangeRecorder(IScheduleChangeStore store, IStudentTimetableStore timetable,
    ScheduleConflictChecker checker)
{
    public async Task RecordAsync(ScheduleChange change, DateTimeOffset rosterInstantUtc,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> newTimes, Guid? excludeSessionId,
        DateTimeOffset now, CancellationToken token)
    {
        var roster = await timetable.ListRosterAtAsync(change.CenterId, change.ClassId, rosterInstantUtc, token);
        var conflicts = await checker.ConflictCountsAsync(change.CenterId, change.ClassId, roster, newTimes,
            excludeSessionId, token);
        var emails = await store.ReadEmailsAsync(roster.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToArray(), token);
        var notices = roster.Select(student => ScheduleNotice.Create(change, student.StudentId, student.UserId,
            conflicts.GetValueOrDefault(student.StudentId), student.UserId is { } user && emails.ContainsKey(user), now)).ToArray();
        await store.AddAsync(change, notices, token);
        if (notices.All(x => !x.EmailQueued)) return;

        var labels = await timetable.ReadClassLabelsAsync([change.ClassId], token);
        var centers = await timetable.ReadCenterNamesAsync([change.CenterId], token);
        var label = labels.Count > 0 ? labels[0] : null;
        var center = centers.GetValueOrDefault(change.CenterId) ?? "Trung tâm";
        var outgoing = roster.Join(notices.Where(x => x.EmailQueued), x => x.StudentId, x => x.StudentId, (student, notice) =>
            new OutgoingEmail(emails[student.UserId!.Value],
                Subject(change, center, label),
                Body(change, student.FullName, center, label, notice.ConflictCount, store.StudentPortalUrl))).ToArray();
        await store.QueueEmailsAsync(outgoing, token);
    }

    /// <summary>
    /// The center added a student who has a login to a class: one notice and one email for that student only, in
    /// the caller's transaction.
    /// </summary>
    public async Task RecordEnrollmentAsync(Guid centerId, Guid classId, Guid studentId, Guid userId, string fullName,
        Guid? actorUserId, DateTimeOffset now, CancellationToken token)
    {
        var change = ScheduleChange.ForEnrollment(centerId, classId, actorUserId, now);
        var emails = await store.ReadEmailsAsync([userId], token);
        var notice = ScheduleNotice.Create(change, studentId, userId, 0, emails.ContainsKey(userId), now);
        await store.AddAsync(change, [notice], token);
        if (!notice.EmailQueued) return;
        var labels = await timetable.ReadClassLabelsAsync([classId], token);
        var centers = await timetable.ReadCenterNamesAsync([centerId], token);
        var label = labels.Count > 0 ? labels[0] : null;
        var center = centers.GetValueOrDefault(centerId) ?? "Trung tâm";
        await store.QueueEmailsAsync([new OutgoingEmail(emails[userId], Subject(change, center, label),
            Body(change, fullName, center, label, 0, store.StudentPortalUrl))], token);
    }

    public static string Describe(ScheduleChangeKind kind) => kind switch
    {
        ScheduleChangeKind.StudentEnrolled => "thêm bạn vào",
        ScheduleChangeKind.SessionAdded => "thêm buổi học",
        ScheduleChangeKind.SessionRescheduled => "dời lịch một buổi học",
        ScheduleChangeKind.SessionCancelled => "huỷ một buổi học",
        ScheduleChangeKind.SessionNotHeld => "ghi nhận một buổi học không diễn ra",
        ScheduleChangeKind.MakeupScheduled => "xếp buổi học bù",
        ScheduleChangeKind.SeriesStopped => "dừng lịch học định kỳ",
        ScheduleChangeKind.SeriesReplaced => "thay đổi lịch học định kỳ",
        _ => "thay đổi lịch học"
    };

    private static string Subject(ScheduleChange change, string center, ClassLabelRow? label)
    {
        var text = $"{(change.IsUrgent ? "[Báo gấp] " : "")}{center}: {Describe(change.Kind)} lớp {label?.Name ?? ""}".Trim();
        return text.Length <= 200 ? text : text[..200];
    }

    private static string Body(ScheduleChange change, string studentName, string center, ClassLabelRow? label,
        int conflictCount, string? portalUrl)
    {
        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"Chào {studentName},").AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"{center} vừa {Describe(change.Kind)} lớp {label?.Name} ({label?.Code}).");
        if (change.OldStartUtc is { } oldStart && change.OldEndUtc is { } oldEnd)
            body.AppendLine(change.NewStartUtc is { } ns && change.NewEndUtc is { } ne
                ? string.Create(CultureInfo.InvariantCulture, $"Lịch cũ: {CenterTime.FormatSlot(oldStart, oldEnd)} → Lịch mới: {CenterTime.FormatSlot(ns, ne)}.")
                : string.Create(CultureInfo.InvariantCulture, $"Buổi học: {CenterTime.FormatSlot(oldStart, oldEnd)}."));
        else if (change.NewStartUtc is { } start && change.NewEndUtc is { } end)
            body.AppendLine(CultureInfo.InvariantCulture, $"Buổi học: {CenterTime.FormatSlot(start, end)}.");
        if (change.Kind is ScheduleChangeKind.SeriesStopped or ScheduleChangeKind.SeriesReplaced)
        {
            body.AppendLine(CultureInfo.InvariantCulture, $"Số buổi bị ảnh hưởng: {change.AffectedSessionCount}.");
            if (change.FirstAffectedStartUtc is { } first)
                body.AppendLine(CultureInfo.InvariantCulture, $"Áp dụng từ buổi: {CenterTime.FormatStart(first)}.");
        }
        if (!string.IsNullOrWhiteSpace(change.Reason)) body.AppendLine(CultureInfo.InvariantCulture, $"Lý do: {change.Reason}");
        if (conflictCount > 0)
            body.AppendLine(CultureInfo.InvariantCulture, $"Lưu ý: lịch mới trùng với {conflictCount} buổi học khác của bạn.");
        body.AppendLine();
        body.AppendLine("Bạn có thể xác nhận đã xem, xin vắng, xin học bù, xin chuyển lớp hoặc xin rút trong cổng học sinh.");
        if (!string.IsNullOrWhiteSpace(portalUrl)) body.AppendLine(portalUrl);
        var text = body.ToString();
        return text.Length <= 4000 ? text : text[..4000];
    }
}

/// <summary>Reading the change log (center) and the notices (student).</summary>
public sealed class ScheduleChangeQueries(IScheduleChangeStore store, IStudentTimetableStore timetable,
    ITeachingNameReader names, IClock clock, IUserDirectory? users = null)
{
    public async Task<IReadOnlyList<ScheduleChangeDto>> SessionHistoryAsync(Guid centerId, Guid sessionId,
        CancellationToken token)
    {
        var items = await store.ListSessionChangesAsync(centerId, sessionId, token);
        return await DecorateAsync(centerId, items, withResponses: false, token);
    }

    public async Task<TeachingPage<ScheduleChangeDto>> CenterChangesAsync(Guid centerId, Guid? classId, int page,
        int pageSize, CancellationToken token)
    {
        var (items, total) = await store.ListCenterChangesAsync(centerId, classId, page, pageSize, token);
        return new TeachingPage<ScheduleChangeDto>
        {
            Page = page, PageSize = pageSize, TotalCount = total,
            Items = (await DecorateAsync(centerId, items, withResponses: true, token)).ToList()
        };
    }

    public async Task<MyNoticePage> MyNoticesAsync(Guid userId, bool unreadOnly, int page, int pageSize,
        CancellationToken token)
    {
        var (rows, total, unread) = await store.ListMyNoticesAsync(userId, unreadOnly, page, pageSize, token);
        var labels = (await timetable.ReadClassLabelsAsync(rows.Select(x => x.Change.ClassId).Distinct().ToArray(), token))
            .ToDictionary(x => x.ClassId);
        var centers = await timetable.ReadCenterNamesAsync(rows.Select(x => x.Change.CenterId).Distinct().ToArray(), token);
        var items = rows.Select(row =>
        {
            var c = row.Change;
            var label = labels.GetValueOrDefault(c.ClassId);
            return new MyNoticeDto(row.Notice.Id, c.CenterId, c.ClassId, c.Kind.ToString(), c.SessionId,
                c.OldStartUtc, c.OldEndUtc, c.NewStartUtc, c.NewEndUtc, c.Reason, row.Notice.IsUrgent,
                c.AffectedSessionCount, c.FirstAffectedStartUtc, row.Notice.ConflictCount, row.Notice.CreatedAtUtc,
                row.Notice.SeenAtUtc, row.Notice.Response.ToString())
            {
                CenterName = centers.GetValueOrDefault(c.CenterId), ClassCode = label?.Code, ClassName = label?.Name
            };
        }).ToArray();
        return new MyNoticePage(page, pageSize, total, unread, items);
    }

    /// <summary>"Đã xem, vẫn đi được".</summary>
    public async Task<Result<string>> AcknowledgeAsync(Guid userId, Guid noticeId, CancellationToken token)
    {
        var notice = await store.FindMyNoticeAsync(userId, noticeId, token);
        if (notice is null) return Result<string>.Failure(ScheduleChangeErrors.NoticeNotFound);
        notice.Acknowledge(clock.UtcNow);
        await store.SaveAsync(token);
        return Result<string>.Success(notice.Response.ToString());
    }

    public Task<int> MarkAllSeenAsync(Guid userId, CancellationToken token) =>
        store.MarkAllSeenAsync(userId, clock.UtcNow, token);

    private async Task<IReadOnlyList<ScheduleChangeDto>> DecorateAsync(Guid centerId, IReadOnlyList<ScheduleChange> items,
        bool withResponses, CancellationToken token)
    {
        if (items.Count == 0) return [];
        var lookup = await names.ReadAsync(centerId, items.Select(x => x.ClassId),
            items.SelectMany(x => new[] { x.OldTeacherId, x.NewTeacherId }).Where(x => x.HasValue).Select(x => x!.Value),
            items.SelectMany(x => new[] { x.OldRoomId, x.NewRoomId }).Where(x => x.HasValue).Select(x => x!.Value), token);
        var actors = users is null ? new Dictionary<Guid, string>()
            : await users.GetDisplayNamesAsync(items.Where(x => x.ActorUserId.HasValue).Select(x => x.ActorUserId!.Value), token);
        var responses = withResponses
            ? await store.CountResponsesAsync(centerId, items.Select(x => x.Id).ToArray(), token)
            : new Dictionary<Guid, NoticeResponseCounts>();
        return items.Select(x =>
        {
            var label = lookup.Class(x.ClassId);
            return ScheduleChangeDto.From(x) with
            {
                ClassCode = label?.Code, ClassName = label?.Name,
                OldTeacherName = lookup.Teacher(x.OldTeacherId), NewTeacherName = lookup.Teacher(x.NewTeacherId),
                OldRoomName = x.OldRoomId is { } oldRoom ? lookup.Room(oldRoom) : null,
                NewRoomName = x.NewRoomId is { } newRoom ? lookup.Room(newRoom) : null,
                ActorName = x.ActorUserId is { } actor ? actors.GetValueOrDefault(actor) : null,
                Responses = responses.GetValueOrDefault(x.Id)
            };
        }).ToArray();
    }
}
