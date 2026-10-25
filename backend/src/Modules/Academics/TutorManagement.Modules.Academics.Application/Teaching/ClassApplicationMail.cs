using System.Globalization;
using System.Text;

namespace TutorManagement.Modules.Academics.Application.Teaching;

/// <summary>
/// Tells the student by email when the center decides on a class application. Queued on the registration
/// transaction's connection, so a rolled-back decision sends nothing; students without a login email get nothing.
/// </summary>
public sealed class ClassApplicationMail(IScheduleChangeStore store, IStudentTimetableStore timetable)
{
    public Task ApprovedAsync(Guid centerId, Guid classId, Guid userId, string fullName, int otherCenterConflicts,
        CancellationToken token) =>
        QueueAsync(centerId, classId, userId, approved: true, (center, label) =>
        {
            var body = new StringBuilder();
            body.AppendLine(CultureInfo.InvariantCulture, $"Chào {fullName},").AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture, $"{center} đã duyệt đơn đăng ký lớp {label} của bạn. Bạn đã có tên trong lớp; lịch học hiện trong cổng học sinh.");
            if (otherCenterConflicts > 0)
                body.AppendLine(CultureInfo.InvariantCulture, $"Lưu ý: lớp này có {otherCenterConflicts} buổi trùng giờ với lịch học ở trung tâm khác của bạn.");
            return body;
        }, token);

    public Task RejectedAsync(Guid centerId, Guid classId, Guid userId, string fullName, string reason,
        CancellationToken token) =>
        QueueAsync(centerId, classId, userId, approved: false, (center, label) =>
        {
            var body = new StringBuilder();
            body.AppendLine(CultureInfo.InvariantCulture, $"Chào {fullName},").AppendLine();
            body.AppendLine(CultureInfo.InvariantCulture, $"{center} chưa nhận đơn đăng ký lớp {label} của bạn.");
            body.AppendLine(CultureInfo.InvariantCulture, $"Lý do: {reason.Trim()}");
            body.AppendLine().AppendLine("Bạn có thể xem các lớp khác đang mở đăng ký trong cổng học sinh.");
            return body;
        }, token);

    private async Task QueueAsync(Guid centerId, Guid classId, Guid userId, bool approved,
        Func<string, string, StringBuilder> compose, CancellationToken token)
    {
        var emails = await store.ReadEmailsAsync([userId], token);
        if (!emails.TryGetValue(userId, out var recipient)) return;
        var labels = await timetable.ReadClassLabelsAsync([classId], token);
        var label = labels.Count > 0 ? $"{labels[0].Name} ({labels[0].Code})" : "đã chọn";
        var center = (await timetable.ReadCenterNamesAsync([centerId], token)).GetValueOrDefault(centerId) ?? "Trung tâm";
        var body = compose(center, label);
        if (!string.IsNullOrWhiteSpace(store.StudentPortalUrl)) body.AppendLine().AppendLine(store.StudentPortalUrl);
        var subject = $"{center}: đơn đăng ký lớp {label} {(approved ? "đã được duyệt" : "chưa được nhận")}";
        var text = body.ToString();
        await store.QueueEmailsAsync([new OutgoingEmail(recipient, subject.Length <= 200 ? subject : subject[..200],
            text.Length <= 4000 ? text : text[..4000])], token);
    }
}
