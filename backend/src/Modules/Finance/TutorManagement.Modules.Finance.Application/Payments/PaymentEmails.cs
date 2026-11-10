using System.Globalization;
using System.Text;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>D6: the confirmation a student (or the parent using the student's login) gets when a transfer is booked.</summary>
public static class PaymentEmails
{
    public static OutgoingTuitionEmail Received(CenterLedger ledger, TuitionStudentRow student, Receipt receipt, string recipient,
        string centerName, string? portalUrl)
    {
        var owed = ledger.OpenCharges(student.Id).Sum(ledger.Remaining);
        var credit = ledger.CreditOf(student.Id);
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Chào {student.FullName},");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture,
            $"{centerName} đã nhận {TuitionReminders.Money(receipt.Amount)} chuyển khoản ngày {receipt.ReceivedOn:dd/MM/yyyy}.");
        text.AppendLine(CultureInfo.InvariantCulture, $"Biên lai: {Receipt.FormatNumber(receipt.Number)}");
        text.AppendLine(owed > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Học phí còn lại: {TuitionReminders.Money(owed)}")
            : "Bạn đã đóng đủ học phí hiện tại.");
        if (credit > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Số dư được trừ vào kỳ sau: {TuitionReminders.Money(credit)}");
        if (portalUrl is not null) text.AppendLine(CultureInfo.InvariantCulture, $"Xem chi tiết: {portalUrl}/tuition");
        text.AppendLine();
        text.AppendLine("Nếu bạn không thực hiện khoản chuyển này, vui lòng liên hệ trung tâm.");
        return new OutgoingTuitionEmail(recipient, $"Đã nhận học phí — {centerName}", text.ToString());
    }

    /// <summary>Queues the confirmation for each booked receipt whose student has a login with an email.</summary>
    public static async Task QueueAsync(ITuitionStore store, ITuitionAcademicsReader academics, CenterLedger ledger,
        IReadOnlyList<Receipt> booked, CancellationToken token)
    {
        if (booked.Count == 0) return;
        var students = booked.Select(x => ledger.FindStudent(x.StudentId)).Where(x => x?.UserId is not null).ToArray();
        if (students.Length == 0) return;
        var emails = await store.ReadEmailsAsync(students.Select(x => x!.UserId!.Value).ToArray(), token);
        var centerName = (await academics.ReadCenterNamesAsync([ledger.CenterId], token)).GetValueOrDefault(ledger.CenterId)
                         ?? string.Empty;
        var outgoing = booked
            .Select(x => (Receipt: x, Student: ledger.FindStudent(x.StudentId)))
            .Where(x => x.Student?.UserId is { } user && emails.ContainsKey(user))
            .Select(x => Received(ledger, x.Student!, x.Receipt, emails[x.Student!.UserId!.Value], centerName, store.StudentPortalUrl))
            .ToArray();
        if (outgoing.Length > 0) await store.QueueEmailsAsync(outgoing, token);
    }
}
