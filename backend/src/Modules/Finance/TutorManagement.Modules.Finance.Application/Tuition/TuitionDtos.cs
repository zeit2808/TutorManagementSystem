namespace TutorManagement.Modules.Finance.Application.Tuition;

// DTOs are classes with settable members: the XML formatter needs a parameterless constructor for nested types.

public sealed class TuitionPage<T>
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public List<T> Items { get; set; } = [];
}

public sealed class ChargeDto
{
    public Guid Id { get; set; }
    public Guid ClassId { get; set; }
    public string ClassCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    /// <summary>1-based cycle number of the class ("Kỳ 3").</summary>
    public int CycleNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public long MonthlyFee { get; set; }
    public long ListAmount { get; set; }
    public long DiscountAmount { get; set; }
    public long Amount { get; set; }
    public long Paid { get; set; }
    public long Remaining { get; set; }
    public DateOnly DueDate { get; set; }
    /// <summary>Paid, Partial, Unpaid or Overdue.</summary>
    public string Status { get; set; } = string.Empty;
    public int? JoinSessions { get; set; }
    public int? PeriodSessions { get; set; }
    public DateOnly? EndedOn { get; set; }
    public int? EndedSessionsAttended { get; set; }
    public int? EndedSessionsTotal { get; set; }
    public long? EndedValue { get; set; }
    public long WaivedAmount { get; set; }
    public DateTimeOffset? RemindedAtUtc { get; set; }
    /// <summary>H18: sessions of this cycle the student missed or the center did not hold.</summary>
    public List<MissedSessionDto> Missed { get; set; } = [];
    public int BilledSessions { get; set; }
    /// <summary>Suggested reduction per missed session: (amount before reduction) ÷ billed sessions, rounded to 1,000đ.</summary>
    public long PerSessionAmount { get; set; }
    public long AdjustmentAmount { get; set; }
    public int? AdjustmentSessions { get; set; }
    public string? AdjustmentReason { get; set; }
    public DateTimeOffset? AdjustedAtUtc { get; set; }
    public string? AdjustedByName { get; set; }
}

public sealed class MissedSessionDto
{
    public Guid SessionId { get; set; }
    public DateTimeOffset StartUtc { get; set; }
    /// <summary>ExcusedAbsent, UnexcusedAbsent (the student) or Cancelled, NotHeld (the center).</summary>
    public string Kind { get; set; } = string.Empty;
    public string? Reason { get; set; }
    /// <summary>Cancelled / not held and a makeup session was scheduled for it.</summary>
    public bool HasMakeup { get; set; }
}

public sealed class ReceiptLineDto
{
    public Guid ChargeId { get; set; }
    public string ClassCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public int CycleNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public long Amount { get; set; }
}

public sealed class ReceiptDto
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string NumberText { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? PayerName { get; set; }
    /// <summary>Cash or BankTransfer.</summary>
    public string Method { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long Allocated { get; set; }
    /// <summary>Part kept as the student's credit.</summary>
    public long Credit { get; set; }
    public DateOnly ReceivedOn { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public string? VoidedByName { get; set; }
    public string? VoidReason { get; set; }
    public List<ReceiptLineDto> Lines { get; set; } = [];
}

public sealed class DiscountDto
{
    public Guid Id { get; set; }
    public Guid StudentId { get; set; }
    public Guid ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    /// <summary>Percent or Amount.</summary>
    public string Kind { get; set; } = string.Empty;
    public long Value { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class StudentClassCycleDto
{
    public Guid ClassId { get; set; }
    public string ClassCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public bool Active { get; set; }
    /// <summary>The cycle running today (null before the class starts).</summary>
    public DateOnly? CurrentPeriodStart { get; set; }
    public DateOnly? CurrentPeriodEnd { get; set; }
    public DateOnly? NextPeriodStart { get; set; }
    public long? MonthlyFee { get; set; }
}

public sealed class BankAccountDto
{
    public string BankBin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
}

public sealed class StudentTuitionDto
{
    public Guid CenterId { get; set; }
    public string CenterName { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public string? ParentName { get; set; }
    public string? ParentPhone { get; set; }
    public bool HasAccount { get; set; }
    public long Owed { get; set; }
    public long Overdue { get; set; }
    public long Credit { get; set; }
    /// <summary>VietQR transfer content for this student ("HS000123 HOC PHI").</summary>
    public string TransferContent { get; set; } = string.Empty;
    public BankAccountDto? Bank { get; set; }
    public List<StudentClassCycleDto> Classes { get; set; } = [];
    public List<ChargeDto> Charges { get; set; } = [];
    public List<ReceiptDto> Receipts { get; set; } = [];
    public List<DiscountDto> Discounts { get; set; } = [];
}

public sealed class ClassFeeHistoryDto
{
    public long MonthlyFee { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? CreatedByName { get; set; }
}

public sealed class ClassFeeRowDto
{
    public Guid ClassId { get; set; }
    public string ClassCode { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public bool IsArchived { get; set; }
    /// <summary>Fee of the cycle running today, or the latest fee before the class starts.</summary>
    public long? CurrentFee { get; set; }
    /// <summary>A newer fee that starts with the next cycle.</summary>
    public long? NextFee { get; set; }
    public DateOnly? NextFeeFrom { get; set; }
    public DateOnly? FirstSessionDate { get; set; }
    public DateOnly? CurrentPeriodStart { get; set; }
    public DateOnly? CurrentPeriodEnd { get; set; }
    public int ActiveStudents { get; set; }
    public long CurrentCycleBilled { get; set; }
    public long CurrentCyclePaid { get; set; }
    public List<ClassFeeHistoryDto> History { get; set; } = [];
}

public sealed class TuitionSummaryDto
{
    public long Owed { get; set; }
    public long Overdue { get; set; }
    public int OverdueStudents { get; set; }
    public int UnpaidCharges { get; set; }
    public long CollectedThisMonth { get; set; }
    public long Credit { get; set; }
    public int ClassesWithoutFee { get; set; }
    public DateOnly Today { get; set; }
}

public sealed class FinanceSettingsDto
{
    public string? BankBin { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankAccountName { get; set; }
    public int DueDays { get; set; }
    public bool EmailReminders { get; set; }
    public int Version { get; set; }
}

public sealed class MyTuitionDto
{
    public List<StudentTuitionDto> Centers { get; set; } = [];
}
