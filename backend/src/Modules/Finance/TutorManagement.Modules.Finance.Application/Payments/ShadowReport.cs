using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Payments;

public sealed class ShadowReportRowDto
{
    public Guid TransactionId { get; init; }
    public DateTimeOffset TransactedAtUtc { get; init; }
    public long Amount { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ReferenceCode { get; init; }
    public string? ShadowOutcome { get; init; }
    public string? ShadowReason { get; init; }
    public string? ExpectedStudentCode { get; init; }
    public string? ExpectedStudentName { get; init; }
    public Guid? ManualReceiptId { get; init; }
    public string? ManualReceiptNumber { get; init; }
    public string? ManualStudentName { get; init; }

    /// <summary>
    /// Match: same student and amount as a hand-written receipt. DifferentStudent: staff gave it to someone else.
    /// MissingManual: Auto would book it, staff did not. ManualOnly: staff booked it, Auto would not.
    /// BothSkipped: neither booked it. NotEvaluated: Shadow has not looked at it yet.
    /// </summary>
    public string Verdict { get; init; } = string.Empty;
}

public sealed class ShadowManualReceiptDto
{
    public Guid ReceiptId { get; init; }
    public string Number { get; init; } = string.Empty;
    public DateOnly ReceivedOn { get; init; }
    public long Amount { get; init; }
    public string StudentName { get; init; } = string.Empty;
}

/// <summary>
/// K18: what Shadow would have booked next to what staff recorded by hand, so the owner switches to Auto only when
/// they agree. Bank-transfer receipts with no pulled transfer near them are listed too.
/// </summary>
public sealed class ShadowReportDto
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public Dictionary<string, int> Counts { get; init; } = [];
    public List<ShadowReportRowDto> Rows { get; init; } = [];
    public List<ShadowManualReceiptDto> ManualWithoutTransfer { get; init; } = [];

    /// <summary>True when every evaluated transfer is Match or BothSkipped and no hand-written transfer is unexplained.</summary>
    public bool Agrees { get; init; }
}

public static class ShadowReport
{
    public const int DayWindow = 3;
    public const int MaxDays = 93;

    public static ShadowReportDto Build(DateOnly from, DateOnly to, IReadOnlyList<BankTransaction> transfers,
        IReadOnlyList<Receipt> receipts, IReadOnlyList<TuitionStudentRow> students)
    {
        var byId = students.ToDictionary(x => x.Id);
        Guid Resolve(Guid id)
        {
            var current = id;
            for (var hop = 0; hop < 10 && byId.TryGetValue(current, out var row) && row.MergedIntoStudentId is { } next; hop++)
                current = next;
            return current;
        }

        var manual = receipts.Where(x => !x.IsVoided && x.Source == ReceiptSource.Manual && x.BankTransactionId is null &&
                                         x.Method == PaymentMethod.BankTransfer &&
                                         x.ReceivedOn >= from.AddDays(-DayWindow) && x.ReceivedOn <= to.AddDays(DayWindow))
            .OrderBy(x => x.ReceivedOn).ThenBy(x => x.Number).ToList();
        var unused = manual.ToHashSet();
        var rows = new List<ShadowReportRowDto>();

        Receipt? Take(BankTransaction transfer, Func<Receipt, bool> fits)
        {
            var day = FinanceTime.LocalDate(transfer.TransactedAtUtc);
            var found = unused.Where(x => x.Amount == transfer.Amount && Math.Abs(x.ReceivedOn.DayNumber - day.DayNumber) <= DayWindow && fits(x))
                .OrderBy(x => Math.Abs(x.ReceivedOn.DayNumber - day.DayNumber)).ThenBy(x => x.Number).FirstOrDefault();
            if (found is not null) unused.Remove(found);
            return found;
        }

        Guid? Expected(BankTransaction x) =>
            x.ShadowEvaluatedAtUtc is not null && x.ShadowOutcome == "Book" && x.ShadowStudentId is { } s ? Resolve(s) : null;
        var ordered = transfers.OrderBy(x => x.TransactedAtUtc).ThenBy(x => x.Id).ToArray();

        // Pass 1: every expected booking takes a receipt of its own student, so a transfer for someone else can never
        // borrow it first. Pass 2: what is left pairs by amount and date only.
        var exact = new Dictionary<Guid, Receipt>();
        foreach (var transfer in ordered)
            if (Expected(transfer) is { } student && Take(transfer, x => Resolve(x.StudentId) == student) is { } own)
                exact[transfer.Id] = own;

        foreach (var transfer in ordered)
        {
            var expected = Expected(transfer);
            Receipt? paired;
            string verdict;
            if (transfer.ShadowEvaluatedAtUtc is null) { paired = null; verdict = "NotEvaluated"; }
            else if (expected is not null)
            {
                if (exact.TryGetValue(transfer.Id, out paired)) verdict = "Match";
                else
                {
                    paired = Take(transfer, _ => true);
                    verdict = paired is null ? "MissingManual" : "DifferentStudent";
                }
            }
            else
            {
                paired = Take(transfer, _ => true);
                verdict = paired is null ? "BothSkipped" : "ManualOnly";
            }
            var expectedRow = expected is { } e ? byId.GetValueOrDefault(e) : null;
            rows.Add(new ShadowReportRowDto
            {
                TransactionId = transfer.Id, TransactedAtUtc = transfer.TransactedAtUtc, Amount = transfer.Amount,
                Content = transfer.Content, ReferenceCode = transfer.ReferenceCode, ShadowOutcome = transfer.ShadowOutcome,
                ShadowReason = transfer.ShadowReason, ExpectedStudentCode = expectedRow?.Code, ExpectedStudentName = expectedRow?.FullName,
                ManualReceiptId = paired?.Id, ManualReceiptNumber = paired is null ? null : Receipt.FormatNumber(paired.Number),
                ManualStudentName = paired is null ? null : byId.GetValueOrDefault(Resolve(paired.StudentId))?.FullName,
                Verdict = verdict
            });
        }

        var leftover = unused.Where(x => x.ReceivedOn >= from && x.ReceivedOn <= to)
            .Select(x => new ShadowManualReceiptDto
            {
                ReceiptId = x.Id, Number = Receipt.FormatNumber(x.Number), ReceivedOn = x.ReceivedOn, Amount = x.Amount,
                StudentName = byId.GetValueOrDefault(Resolve(x.StudentId))?.FullName ?? string.Empty
            }).ToList();
        rows = [.. rows.OrderBy(x => x.TransactedAtUtc)];
        return new ShadowReportDto
        {
            From = from, To = to, Rows = rows, ManualWithoutTransfer = leftover,
            Counts = rows.GroupBy(x => x.Verdict).ToDictionary(x => x.Key, x => x.Count()),
            Agrees = rows.All(x => x.Verdict is "Match" or "BothSkipped") && leftover.Count == 0
        };
    }
}
