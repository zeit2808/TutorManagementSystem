using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;

namespace TutorManagement.IntegrationTests;

/// <summary>QUY-TAC wave 3 rules that need no database.</summary>
public sealed class ScheduleRulesDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 3, 0, 0, TimeSpan.Zero);

    private static TeachingSession Session(DateTimeOffset start) => TeachingSession.Create(Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), Guid.NewGuid(), start, start.AddMinutes(90), Now.AddDays(-1));

    [Fact]
    public void A_single_session_change_within_24_hours_is_urgent()
    {
        var soon = Session(Now.AddHours(23));
        Assert.True(ScheduleChange.ForSession(ScheduleChangeKind.SessionCancelled, soon, soon.StartUtc, soon.EndUtc,
            soon.TeacherId, soon.RoomId, "Ốm", null, Now).IsUrgent);
        var later = Session(Now.AddHours(25));
        Assert.False(ScheduleChange.ForSession(ScheduleChangeKind.SessionCancelled, later, later.StartUtc, later.EndUtc,
            later.TeacherId, later.RoomId, "Ốm", null, Now).IsUrgent);
        // Moving a far session to tomorrow morning is urgent because of the new time.
        var moved = ScheduleChange.ForSession(ScheduleChangeKind.SessionRescheduled, Session(Now.AddHours(10)),
            Now.AddDays(5), Now.AddDays(5).AddHours(1), Guid.NewGuid(), Guid.NewGuid(), "Đổi giờ", null, Now);
        Assert.True(moved.IsUrgent);
        Assert.Equal(Now.AddDays(5), moved.OldStartUtc);
    }

    [Fact]
    public void Series_changes_need_seven_days_notice()
    {
        Assert.True(ScheduleChange.IsShortNotice(Now.AddDays(6), Now));
        Assert.False(ScheduleChange.IsShortNotice(Now.AddDays(7), Now));
        Assert.False(ScheduleChange.IsShortNotice(null, Now));
        Assert.Throws<ArgumentException>(() => ScheduleChange.ForSeries(ScheduleChangeKind.SeriesStopped, Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), 3, Now.AddDays(1), null, " ", null, Now));
    }

    [Fact]
    public void Requests_name_a_session_or_target_only_when_their_kind_needs_it()
    {
        var ids = (Center: Guid.NewGuid(), Student: Guid.NewGuid(), User: Guid.NewGuid(), Class: Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => StudentRequest.Create(ids.Center, ids.Student, ids.User, ids.Class,
            StudentRequestKind.ExcusedAbsence, null, null, null, "Ốm", Now));
        Assert.Throws<ArgumentException>(() => StudentRequest.Create(ids.Center, ids.Student, ids.User, ids.Class,
            StudentRequestKind.Transfer, null, ids.Class, null, "Chuyển", Now));
        Assert.Throws<ArgumentException>(() => StudentRequest.Create(ids.Center, ids.Student, ids.User, ids.Class,
            StudentRequestKind.Withdrawal, null, null, null, new string('x', 501), Now));
        var request = StudentRequest.Create(ids.Center, ids.Student, ids.User, ids.Class,
            StudentRequestKind.Withdrawal, null, null, null, "  Bận học thêm  ", Now);
        Assert.Equal("Bận học thêm", request.Reason);
        request.Approve(Guid.NewGuid(), null, Now);
        Assert.Throws<InvalidOperationException>(() => request.Cancel(Now));
        Assert.Equal(1, request.Version);
    }

    [Fact]
    public void Acknowledging_after_a_request_keeps_the_request_answer()
    {
        var change = ScheduleChange.ForSession(ScheduleChangeKind.SessionCancelled, Session(Now.AddDays(3)),
            Now.AddDays(3), Now.AddDays(3).AddHours(1), Guid.NewGuid(), Guid.NewGuid(), "Nghỉ", null, Now);
        var notice = ScheduleNotice.Create(change, Guid.NewGuid(), null, 0, emailQueued: true, Now);
        Assert.False(notice.EmailQueued); // no account, no email
        notice.RecordRequest(Now);
        notice.Acknowledge(Now.AddMinutes(1));
        Assert.Equal(NoticeResponse.Requested, notice.Response);
        Assert.Equal(Now, notice.SeenAtUtc);
    }

    [Fact]
    public void Vietnam_time_is_used_in_notice_text()
    {
        var start = new DateTimeOffset(2026, 10, 27, 11, 0, 0, TimeSpan.Zero); // Tue 18:00 in Vietnam
        Assert.Equal("Thứ Ba 27/10/2026 18:00–19:30", CenterTime.FormatSlot(start, start.AddMinutes(90)));
        Assert.Equal("Thứ Ba 27/10/2026 18:00", CenterTime.FormatStart(start));
    }
}
