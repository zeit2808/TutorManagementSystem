using System.Globalization;
using System.Text;
using TutorManagement.Application.Abstractions.Clock;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>H14: a student sees what to pay, what was paid and how to transfer, in every center (read only).</summary>
public sealed class StudentTuitionQuery(ITuitionStore store, ITuitionAcademicsReader reader, IClock clock)
{
    public async Task<MyTuitionDto> ListAsync(Guid userId, CancellationToken token)
    {
        var links = await reader.ListProfilesOfUserAsync(userId, token);
        var centerNames = await reader.ReadCenterNamesAsync(links.Select(x => x.CenterId).Distinct().ToArray(), token);
        var result = new MyTuitionDto();
        var empty = new Dictionary<Guid, string>();
        foreach (var link in links)
        {
            await using var guard = await store.BeginCenterGuardAsync(link.CenterId, token);
            var ledger = await CenterLedger.LoadAsync(link.CenterId, store, reader, token);
            var now = clock.UtcNow;
            var today = FinanceTime.LocalDate(now);
            ledger.Sync(today, now);
            await TuitionWorkflow.PersistAsync(ledger, store, token);
            await guard.CommitAsync(token);
            var student = ledger.FindStudent(link.StudentId);
            if (student is null) continue;
            var view = TuitionViews.Student(ledger, student, centerNames.GetValueOrDefault(link.CenterId) ?? string.Empty,
                today, empty);
            // Students see the money, not which staff member typed it in or why a discount was given.
            foreach (var discount in view.Discounts) discount.CreatedByName = null;
            if (view.Charges.Count == 0 && view.Receipts.Count == 0 && view.Classes.All(x => x.MonthlyFee is null)) continue;
            result.Centers.Add(view);
        }
        result.Centers = result.Centers.OrderBy(x => x.CenterName, StringComparer.CurrentCulture).ToList();
        return result;
    }
}

/// <summary>H15: once a day, bill new cycles everywhere and remind students whose charges passed their due date.</summary>
public sealed class TuitionReminders(ITuitionStore store, ITuitionAcademicsReader reader, IClock clock)
{
    public async Task<int> RunAsync(CancellationToken token)
    {
        var sent = 0;
        foreach (var centerId in await store.ListCentersWithFeesAsync(token))
        {
            await using var guard = await store.BeginCenterGuardAsync(centerId, token);
            var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
            var now = clock.UtcNow;
            var today = FinanceTime.LocalDate(now);
            ledger.Sync(today, now);
            var due = ledger.Charges.Where(x => x.RemindedAtUtc is null && today > x.DueDate && ledger.Remaining(x) > 0)
                .GroupBy(x => x.StudentId).ToArray();
            if (due.Length > 0)
            {
                var students = due.Select(x => ledger.FindStudent(x.Key)).OfType<TuitionStudentRow>().ToDictionary(x => x.Id);
                var emails = ledger.Settings.EmailReminders
                    ? await store.ReadEmailsAsync(students.Values.Where(x => x.UserId.HasValue).Select(x => x.UserId!.Value).ToArray(), token)
                    : new Dictionary<Guid, string>();
                var centerName = (await reader.ReadCenterNamesAsync([centerId], token)).GetValueOrDefault(centerId) ?? string.Empty;
                var outgoing = new List<OutgoingTuitionEmail>();
                foreach (var group in due)
                {
                    foreach (var charge in group) charge.MarkReminded(now);
                    if (!students.TryGetValue(group.Key, out var student) || student.UserId is not { } user ||
                        !emails.TryGetValue(user, out var email)) continue;
                    outgoing.Add(new OutgoingTuitionEmail(email, $"Nhắc học phí quá hạn — {centerName}",
                        Body(ledger, student, group, centerName, store.StudentPortalUrl)));
                }
                if (outgoing.Count > 0) await store.QueueEmailsAsync(outgoing, token);
                sent += outgoing.Count;
            }
            await TuitionWorkflow.PersistAsync(ledger, store, token);
            await guard.CommitAsync(token);
        }
        return sent;
    }

    private static string Body(CenterLedger ledger, TuitionStudentRow student, IEnumerable<Domain.Tuition.TuitionCharge> charges,
        string centerName, string? portalUrl)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Chào {student.FullName},");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{centerName} nhắc: các khoản học phí sau đã quá hạn đóng.");
        foreach (var charge in charges.OrderBy(x => x.PeriodStart))
        {
            var cls = ledger.Classes.GetValueOrDefault(charge.ClassId)?.Name ?? string.Empty;
            text.AppendLine(CultureInfo.InvariantCulture,
                $"- {cls} · kỳ {charge.PeriodStart:dd/MM}–{charge.PeriodEnd:dd/MM/yyyy}: còn {Money(ledger.Remaining(charge))}, hạn {charge.DueDate:dd/MM/yyyy}");
        }
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Nội dung chuyển khoản: {TuitionViews.TransferContent(student.Code)}");
        if (portalUrl is not null) text.AppendLine(CultureInfo.InvariantCulture, $"Xem chi tiết và mã QR: {portalUrl}/tuition");
        text.AppendLine();
        text.AppendLine("Nếu bạn đã đóng, vui lòng bỏ qua email này.");
        return text.ToString();
    }

    /// <summary>800000 → "800.000đ".</summary>
    public static string Money(long amount) =>
        amount.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.') + "đ";
}
