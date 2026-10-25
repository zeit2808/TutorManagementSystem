namespace TutorManagement.Modules.Finance.Contracts.Tuition;

/// <summary>Monthly fee in VND (multiple of 1,000). Applies from the next cycle; the first fee also covers the running one.</summary>
public sealed record SetClassFeeRequest(long MonthlyFee);

/// <summary>
/// Money received from one student. <c>chargeIds</c> are paid first, then the oldest open charges; anything left is
/// credit. <c>requestId</c> makes a retried submit record one receipt.
/// </summary>
public sealed record RecordPaymentRequest(long Amount, string Method, DateOnly? ReceivedOn = null, string? PayerName = null,
    string? Note = null, IReadOnlyList<Guid>? ChargeIds = null, Guid? RequestId = null);

public sealed record VoidReceiptRequest(string Reason);

/// <summary>Kind: Percent (value 1–100) or Amount (VND). Applies to cycles starting between fromDate and toDate.</summary>
public sealed record AddDiscountRequest(Guid ClassId, string Kind, long Value, DateOnly FromDate, DateOnly? ToDate,
    string Reason);

/// <summary>H18: reduce one cycle for missed sessions; amount 0 removes the reduction.</summary>
public sealed record AdjustChargeRequest(long Amount, int? Sessions, string? Reason);

public sealed record UpdateFinanceSettingsRequest(string? BankBin, string? BankAccountNumber, string? BankAccountName,
    int DueDays, bool EmailReminders, int ExpectedVersion);
