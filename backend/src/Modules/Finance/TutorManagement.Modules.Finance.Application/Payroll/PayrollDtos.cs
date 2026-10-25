using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payroll;

public static class PayrollErrors
{
    public static readonly Error NotFound = new("payroll_not_found", "The center, month, teacher or entry was not found.");
    public static readonly Error Disabled = new("payroll_disabled", "Payroll is not enabled for this center.");
    public static readonly Error Forbidden = new("payroll_forbidden", "You cannot see or change this part of payroll.");
    public static readonly Error InvalidMonth = new("validation_error", "month must be yyyy-MM within the payroll period.", "month");
    public static readonly Error InvalidConfig = new("validation_error",
        "mode RevenueShare or PerSession; effectiveMonth this month or next (yyyy-MM); reason 3–300 characters.", "effectiveMonth");
    public static readonly Error ConfigMonthClosed = new("payroll_month_closed",
        "That month is already closed; choose next month.");
    public static readonly Error ConfigVersion = new("payroll_concurrent_change",
        "The configuration was changed by another request. Reload and try again.", "expectedVersion");
    public static readonly Error MonthClosed = new("payroll_month_closed",
        "The month is closed. Reopen it, or book the change in the current month.");
    public static readonly Error CannotClose = new("payroll_cannot_close", "The month cannot be closed yet.");
    public static readonly Error CannotReopen = new("payroll_cannot_reopen", "Only the latest closed month can be reopened.");
    public static readonly Error WrongMode = new("payroll_wrong_mode", "This is not used by the month's payroll method.");
    public static readonly Error InvalidReason = new("validation_error", "A reason of 3–300 characters is required.", "reason");
    public static readonly Error InvalidAdjustment = new("validation_error",
        "amount must be a non-zero multiple of 1,000 (±100,000,000); reason 3–300 characters.", "amount");
    public static readonly Error InvalidExpense = new("validation_error",
        "amount positive; spentOn inside the month; category 2–60 and note 3–300 characters.", "amount");
    public static readonly Error InvalidRate = new("validation_error",
        "amount 0–50,000,000 VND, a multiple of 1,000; classId of this center or empty.", "amount");
    public static readonly Error InvalidPayout = new("validation_error",
        "amount positive; method Cash or BankTransfer; paidOn not in the future; note ≤ 300 characters.", "amount");
    public static readonly Error PayoutTooLarge = new("payroll_payout_exceeds",
        "The payout is more than what is left to pay for this closed month.");
    public static readonly Error AlreadyVoided = new("payroll_already_voided", "The entry is already void.");
    public static readonly Error DisputeResolved = new("payroll_dispute_resolved", "The report is already resolved.");
    public static readonly Error ConcurrentChange = new("payroll_concurrent_change",
        "Payroll data was changed by another request. Reload and try again.");
}

public sealed class PayrollConfigChangeDto
{
    public Guid Id { get; init; }
    public bool PreviousEnabled { get; init; }
    public string PreviousMode { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string EffectiveMonth { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string? ActorName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>A1: the switch only — never the center's money.</summary>
public sealed class PayrollConfigDto
{
    public Guid CenterId { get; init; }
    public bool Enabled { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string? EffectiveMonth { get; init; }
    /// <summary>What applies this month (a change may only start next month).</summary>
    public bool EnabledThisMonth { get; init; }
    public string ModeThisMonth { get; init; } = string.Empty;
    public string CurrentMonth { get; init; } = string.Empty;
    public string? LatestClosedMonth { get; init; }
    public int Version { get; init; }
    public List<PayrollConfigChangeDto> History { get; init; } = [];
}

/// <summary>One row of the admin overview: what applies this month and what is scheduled, never money.</summary>
public sealed class PayrollCenterConfigSummaryDto
{
    public Guid CenterId { get; init; }
    public string CenterName { get; init; } = string.Empty;
    public string CenterStatus { get; init; } = string.Empty;
    public bool EnabledThisMonth { get; init; }
    public string ModeThisMonth { get; init; } = string.Empty;
    /// <summary>Set when the latest change starts after this month.</summary>
    public string? NextMonth { get; init; }
    public bool? NextEnabled { get; init; }
    public string? NextMode { get; init; }
    public string? LatestClosedMonth { get; init; }
    public DateTimeOffset? LastChangedAtUtc { get; init; }
    public string? LastChangedBy { get; init; }
}

public sealed class PayrollConfigOverviewDto
{
    public string CurrentMonth { get; init; } = string.Empty;
    public List<PayrollCenterConfigSummaryDto> Centers { get; init; } = [];
}

public sealed class PayrollStatusDto
{
    public bool Enabled { get; init; }
    public string Mode { get; init; } = string.Empty;
    public string CurrentMonth { get; init; } = string.Empty;
    public Guid? MyTeacherId { get; init; }
    public bool CanSeeAll { get; init; }
    public bool CanEdit { get; init; }
    public bool CanCloseAndPay { get; init; }
}

public sealed class PayrollMonthSummaryDto
{
    public string Month { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Teachers { get; init; }
    public int Sessions { get; init; }
    public long Net { get; init; }
    public long Paid { get; init; }
    public int Warnings { get; init; }
    public int OpenDisputes { get; init; }
}

public sealed class PayrollClassLineDto
{
    public Guid ClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public int Sessions { get; init; }
    public int Minutes { get; init; }
    public long Amount { get; init; }
}

public sealed class PayrollSessionItemDto
{
    public Guid SessionId { get; init; }
    public Guid ClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public DateTimeOffset StartUtc { get; init; }
    public int Minutes { get; init; }
    /// <summary>M2 pay for the session; null when no rate is set (blocks closing).</summary>
    public long? Amount { get; init; }
    public bool Substitute { get; init; }
}

public sealed class PayrollCarriedDto
{
    public string FromMonth { get; init; } = string.Empty;
    public long Amount { get; init; }
}

public sealed class PayrollLineDto
{
    public Guid TeacherId { get; init; }
    public string TeacherCode { get; init; } = string.Empty;
    public string TeacherName { get; init; } = string.Empty;
    public int Sessions { get; init; }
    public int Minutes { get; init; }
    public long Base { get; init; }
    public long Adjustments { get; init; }
    public long Carried { get; init; }
    public long Net { get; init; }
    public long Paid { get; init; }
    public long Remaining { get; init; }
    public List<PayrollClassLineDto> Classes { get; init; } = [];
    public List<PayrollSessionItemDto> Items { get; init; } = [];
    public List<PayrollCarriedDto> CarriedFrom { get; init; } = [];
}

/// <summary>M1 money of the month (G9). Hidden from teachers.</summary>
public sealed class PayrollPoolDto
{
    public long Collected { get; init; }
    public long Prepaid { get; init; }
    public long Unassigned { get; init; }
    public long Expenses { get; init; }
    public long Distributable { get; init; }
    public List<PayrollUnassignedDto> UnassignedItems { get; init; } = [];
}

public sealed class PayrollUnassignedDto
{
    public Guid ClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public DateOnly PeriodStart { get; init; }
    public DateOnly PeriodEnd { get; init; }
    public long Amount { get; init; }
}

public sealed class PayrollAdjustmentDto
{
    public Guid Id { get; init; }
    public Guid TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public long Amount { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? CreatedByName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public sealed class SharedExpenseDto
{
    public Guid Id { get; init; }
    public DateOnly SpentOn { get; init; }
    public string Category { get; init; } = string.Empty;
    public long Amount { get; init; }
    public string Note { get; init; } = string.Empty;
    public string? CreatedByName { get; init; }
    public DateTimeOffset? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public sealed class PayrollPayoutDto
{
    public Guid Id { get; init; }
    public string NumberText { get; init; } = string.Empty;
    public string Month { get; init; } = string.Empty;
    public Guid TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public long Amount { get; init; }
    public string Method { get; init; } = string.Empty;
    public DateOnly PaidOn { get; init; }
    public string? Note { get; init; }
    /// <summary>Paid before the month was closed (L7).</summary>
    public bool Advance { get; init; }
    public string? CreatedByName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public sealed class PayrollDisputeDto
{
    public Guid Id { get; init; }
    public Guid TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ResolvedAtUtc { get; init; }
    public string? ResolvedByName { get; init; }
    public string? Resolution { get; init; }
}

public sealed class PayrollUnsubmittedDto
{
    public Guid SessionId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public DateTimeOffset StartUtc { get; init; }
    public string TeacherName { get; init; } = string.Empty;
}

public sealed class PayrollEventDto
{
    public string Kind { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string? ActorName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class PayrollMonthDto
{
    public string Month { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? ClosedAtUtc { get; init; }
    public string? ClosedByName { get; init; }
    public List<string> CloseBlockers { get; init; } = [];
    public bool CanReopen { get; init; }
    /// <summary>Only for owners and managers.</summary>
    public PayrollPoolDto? Pool { get; init; }
    public List<PayrollLineDto> Lines { get; init; } = [];
    public List<PayrollAdjustmentDto> Adjustments { get; init; } = [];
    public List<SharedExpenseDto> Expenses { get; init; } = [];
    public List<PayrollPayoutDto> Payouts { get; init; } = [];
    public List<PayrollDisputeDto> Disputes { get; init; } = [];
    public List<PayrollUnsubmittedDto> Unsubmitted { get; init; } = [];
    public List<PayrollSessionItemDto> MissingRates { get; init; } = [];
    public List<PayrollEventDto> Events { get; init; } = [];
}

public sealed class PayRateDto
{
    public Guid Id { get; init; }
    public Guid TeacherId { get; init; }
    public string TeacherName { get; init; } = string.Empty;
    public Guid? ClassId { get; init; }
    public string? ClassCode { get; init; }
    public long Amount { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public string? CreatedByName { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? VoidedAtUtc { get; init; }
    public string? VoidReason { get; init; }
}

public sealed class MyEarningsMonthDto
{
    public string Month { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Sessions { get; init; }
    public int Minutes { get; init; }
    public long Net { get; init; }
    public long Paid { get; init; }
    public long Remaining { get; init; }
}

public sealed class MyEarningsCenterDto
{
    public Guid CenterId { get; init; }
    public string CenterName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    /// <summary>The caller's teacher profile(s) in that center, to pick their line in a month view.</summary>
    public List<Guid> TeacherIds { get; init; } = [];
    public long YearNet { get; init; }
    public long YearPaid { get; init; }
    public int YearSessions { get; init; }
    public int YearMinutes { get; init; }
    public List<MyEarningsMonthDto> Months { get; init; } = [];
}

/// <summary>C15: one teacher's money in every center that has payroll on, for one year.</summary>
public sealed class MyEarningsDto
{
    public int Year { get; init; }
    public long YearNet { get; init; }
    public long YearPaid { get; init; }
    public int YearSessions { get; init; }
    public int YearMinutes { get; init; }
    public List<MyEarningsMonthDto> Months { get; init; } = [];
    public List<MyEarningsCenterDto> Centers { get; init; } = [];
}
