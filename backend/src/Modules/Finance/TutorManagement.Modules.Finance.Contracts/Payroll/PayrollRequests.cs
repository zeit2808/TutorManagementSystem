namespace TutorManagement.Modules.Finance.Contracts.Payroll;

/// <summary>Platform admin: mode RevenueShare (M1) or PerSession (M2); effectiveMonth yyyy-MM, this month or next.</summary>
public sealed record SetPayrollConfigRequest(bool Enabled, string Mode, string EffectiveMonth, string Reason,
    int ExpectedVersion);

/// <summary>C10: ± VND, a non-zero multiple of 1,000.</summary>
public sealed record AddPayrollAdjustmentRequest(Guid TeacherId, long Amount, string Reason);

/// <summary>G4: a shared cost of the month (M1 only).</summary>
public sealed record AddSharedExpenseRequest(long Amount, DateOnly SpentOn, string Category, string Note);

/// <summary>L2/L3: VND per session; classId empty = the teacher's default rate.</summary>
public sealed record AddPayRateRequest(Guid TeacherId, Guid? ClassId, long Amount, DateOnly EffectiveFrom);

/// <summary>C11: method Cash or BankTransfer; requestId makes a retried submit record one payout.</summary>
public sealed record AddPayrollPayoutRequest(Guid TeacherId, long Amount, string Method, DateOnly? PaidOn = null,
    string? Note = null, Guid? RequestId = null);

public sealed record PayrollReasonRequest(string Reason);

/// <summary>C14: what looks wrong.</summary>
public sealed record AddPayrollDisputeRequest(string Message);

public sealed record ResolvePayrollDisputeRequest(string Note);
