using System.Globalization;
using System.Text;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>Builds read DTOs from a loaded ledger.</summary>
internal static class TuitionViews
{
    public static ChargeDto Charge(CenterLedger ledger, TuitionCharge x, DateOnly today,
        IReadOnlyDictionary<Guid, string>? names = null)
    {
        var billedSessions = ledger.BilledSessions(x);
        var cls = ledger.Classes.GetValueOrDefault(x.ClassId);
        var student = ledger.FindStudent(x.StudentId);
        var paid = ledger.PaidFor(x.Id);
        return new ChargeDto
        {
            Id = x.Id, ClassId = x.ClassId, ClassCode = cls?.Code ?? string.Empty, ClassName = cls?.Name ?? string.Empty,
            StudentId = x.StudentId, StudentCode = student?.Code ?? string.Empty, StudentName = student?.FullName ?? string.Empty,
            CycleNumber = x.CycleIndex + 1, PeriodStart = x.PeriodStart, PeriodEnd = x.PeriodEnd, MonthlyFee = x.MonthlyFee,
            ListAmount = x.ListAmount, DiscountAmount = x.DiscountAmount, Amount = x.Amount, Paid = paid,
            Remaining = Math.Max(0, x.Amount - paid), DueDate = x.DueDate, Status = ledger.ChargeStatus(x, today),
            JoinSessions = x.JoinSessions, PeriodSessions = x.PeriodSessions, EndedOn = x.EndedOn,
            EndedSessionsAttended = x.EndedSessionsAttended, EndedSessionsTotal = x.EndedSessionsTotal,
            EndedValue = x.EndedValue, WaivedAmount = x.WaivedAmount, RemindedAtUtc = x.RemindedAtUtc,
            Missed = [.. ledger.MissedFor(x)], BilledSessions = billedSessions,
            PerSessionAmount = billedSessions == 0 ? 0
                : TuitionMath.Prorate(x.ListAmount - x.DiscountAmount, 1, billedSessions),
            AdjustmentAmount = x.AdjustmentAmount, AdjustmentSessions = x.AdjustmentSessions,
            AdjustmentReason = x.AdjustmentReason, AdjustedAtUtc = x.AdjustedAtUtc,
            AdjustedByName = x.AdjustedByUserId is { } by ? names?.GetValueOrDefault(by) : null
        };
    }

    public static ReceiptDto Receipt(CenterLedger ledger, Receipt x, IReadOnlyDictionary<Guid, string> names)
    {
        var student = ledger.FindStudent(x.StudentId);
        var charges = ledger.Charges.ToDictionary(c => c.Id);
        return new ReceiptDto
        {
            Id = x.Id, Number = x.Number, NumberText = Domain.Tuition.Receipt.FormatNumber(x.Number), StudentId = x.StudentId,
            StudentCode = student?.Code ?? string.Empty, StudentName = student?.FullName ?? string.Empty,
            PayerName = x.PayerName, Method = x.Method.ToString(), Amount = x.Amount, Allocated = x.Allocated,
            Credit = x.IsVoided ? 0 : x.Unallocated, ReceivedOn = x.ReceivedOn, Note = x.Note, CreatedAtUtc = x.CreatedAtUtc,
            CreatedByName = names.GetValueOrDefault(x.CreatedByUserId), VoidedAtUtc = x.VoidedAtUtc,
            VoidedByName = x.VoidedByUserId is { } voider ? names.GetValueOrDefault(voider) : null, VoidReason = x.VoidReason,
            Lines = x.Allocations.Select(line =>
            {
                var charge = charges.GetValueOrDefault(line.ChargeId);
                var cls = charge is null ? null : ledger.Classes.GetValueOrDefault(charge.ClassId);
                return new ReceiptLineDto
                {
                    ChargeId = line.ChargeId, ClassCode = cls?.Code ?? string.Empty, ClassName = cls?.Name ?? string.Empty,
                    CycleNumber = (charge?.CycleIndex ?? 0) + 1, PeriodStart = charge?.PeriodStart ?? default,
                    PeriodEnd = charge?.PeriodEnd ?? default, Amount = line.Amount
                };
            }).OrderBy(l => l.PeriodStart).ThenBy(l => l.ClassName, StringComparer.CurrentCulture).ToList()
        };
    }

    public static DiscountDto Discount(CenterLedger ledger, TuitionDiscount x, IReadOnlyDictionary<Guid, string> names) => new()
    {
        Id = x.Id, StudentId = x.StudentId, ClassId = x.ClassId, ClassName = ledger.Classes.GetValueOrDefault(x.ClassId)?.Name ?? string.Empty,
        Kind = x.Kind.ToString(), Value = x.Value, FromDate = x.FromDate, ToDate = x.ToDate, Reason = x.Reason,
        CreatedAtUtc = x.CreatedAtUtc, CreatedByName = names.GetValueOrDefault(x.CreatedByUserId), RevokedAtUtc = x.RevokedAtUtc
    };

    public static StudentTuitionDto Student(CenterLedger ledger, TuitionStudentRow student, string centerName, DateOnly today,
        IReadOnlyDictionary<Guid, string> names)
    {
        var charges = ledger.Charges.Where(x => x.StudentId == student.Id)
            .OrderByDescending(x => x.PeriodStart).ThenBy(x => ledger.Classes.GetValueOrDefault(x.ClassId)?.Name)
            .Select(x => Charge(ledger, x, today, names)).ToList();
        var enrollments = ledger.Enrollments.Where(x => ledger.Resolve(x.StudentId) == student.Id).ToArray();
        var classes = enrollments.GroupBy(x => x.ClassId).Select(group =>
        {
            var cls = ledger.Classes.GetValueOrDefault(group.Key);
            var cycle = ledger.CurrentCycle(group.Key, today);
            var fee = cycle is { } c ? ClassFee.ForCycle(ledger.FeeHistory(group.Key), c.Start, c.End) : null;
            return new StudentClassCycleDto
            {
                ClassId = group.Key, ClassCode = cls?.Code ?? string.Empty, ClassName = cls?.Name ?? string.Empty,
                Active = group.Any(x => x.WithdrawnAtUtc is null), CurrentPeriodStart = cycle?.Start,
                CurrentPeriodEnd = cycle?.End, NextPeriodStart = cycle?.End.AddDays(1), MonthlyFee = fee?.MonthlyFee
            };
        }).OrderByDescending(x => x.Active).ThenBy(x => x.ClassName, StringComparer.CurrentCulture).ToList();
        var settings = ledger.Settings;
        return new StudentTuitionDto
        {
            CenterId = ledger.CenterId, CenterName = centerName, StudentId = student.Id, StudentCode = student.Code,
            StudentName = student.FullName, ParentName = student.ParentName, ParentPhone = student.ParentPhone,
            HasAccount = student.UserId.HasValue, Owed = charges.Sum(x => x.Remaining),
            Overdue = charges.Where(x => x.Status == "Overdue").Sum(x => x.Remaining), Credit = ledger.CreditOf(student.Id),
            TransferContent = TransferContent(student.Code),
            Bank = settings.HasBankAccount ? new BankAccountDto
            {
                BankBin = settings.BankBin!, AccountNumber = settings.BankAccountNumber!, AccountName = settings.BankAccountName!
            } : null,
            Classes = classes, Charges = charges,
            Receipts = ledger.Receipts.Where(x => x.StudentId == student.Id).OrderByDescending(x => x.ReceivedOn)
                .ThenByDescending(x => x.Number).Select(x => Receipt(ledger, x, names)).ToList(),
            Discounts = ledger.Discounts.Where(x => x.StudentId == student.Id).OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => Discount(ledger, x, names)).ToList()
        };
    }

    /// <summary>Bank-safe transfer content: ASCII letters and digits only ("HS000123 HOC PHI").</summary>
    public static string TransferContent(string studentCode)
    {
        var builder = new StringBuilder();
        foreach (var c in studentCode.Normalize(NormalizationForm.FormD))
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(char.ToUpperInvariant(c));
        var code = builder.Length > 16 ? builder.ToString(0, 16) : builder.ToString();
        return string.Create(CultureInfo.InvariantCulture, $"{code} HOC PHI").Trim();
    }

    public static IEnumerable<Guid> Actors(CenterLedger ledger) =>
        ledger.Receipts.SelectMany(x => x.VoidedByUserId is { } v ? [x.CreatedByUserId, v] : new[] { x.CreatedByUserId })
            .Concat(ledger.Discounts.Select(x => x.CreatedByUserId)).Concat(ledger.Fees.Select(x => x.CreatedByUserId))
            .Concat(ledger.Charges.Where(x => x.AdjustedByUserId.HasValue).Select(x => x.AdjustedByUserId!.Value))
            .Distinct();
}
