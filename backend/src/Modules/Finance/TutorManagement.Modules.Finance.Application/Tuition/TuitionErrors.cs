using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Tuition;

public static class TuitionErrors
{
    public static readonly Error NotFound = new("tuition_not_found", "The class, student, receipt or discount was not found.");
    public static readonly Error InvalidFee = new("validation_error",
        "monthlyFee must be 0–100,000,000 VND and a multiple of 1,000.", "monthlyFee");
    public static readonly Error InvalidPayment = new("validation_error",
        "amount must be positive, method Cash or BankTransfer, payerName ≤ 150 and note ≤ 300 characters.", "amount");
    public static readonly Error InvalidReceivedOn = new("validation_error",
        "receivedOn cannot be in the future or more than a year back.", "receivedOn");
    public static readonly Error InvalidDiscount = new("validation_error",
        "Percent 1–100 or an amount that is a multiple of 1,000; toDate on or after fromDate; reason 3–300 characters.",
        "value");
    public static readonly Error InvalidReason = new("validation_error", "A reason of 3–300 characters is required.", "reason");
    public static readonly Error InvalidSettings = new("validation_error",
        "Give all three bank fields (6-digit BIN, 4–20 letter/digit account, name 2–100) or none; dueDays 0–60.",
        "bankBin");
    public static readonly Error InvalidCharges = new("validation_error",
        "chargeIds must be open charges of this student.", "chargeIds");
    public static readonly Error InvalidAdjustment = new("validation_error",
        "amount must be 0 or a multiple of 1,000 not above the cycle's amount; sessions 0–100; reason 3–300 characters.",
        "amount");
    public static readonly Error ChargeEnded = new("tuition_charge_ended",
        "The student left during this cycle; its amount is already settled.");
    public static readonly Error StudentUnavailable = new("tuition_student_merged",
        "The profile was merged into another one; record money on the kept profile.");
    public static readonly Error NotEnrolled = new("tuition_not_enrolled", "The student has never been in this class.");
    public static readonly Error BankNotConfigured = new("tuition_bank_not_configured",
        "The center has not set up a bank account for transfers.");
    public static readonly Error NothingToPay = new("tuition_nothing_to_pay", "The student has no open charges.");
    public static readonly Error AlreadyVoided = new("receipt_already_voided", "The receipt is already void.");
    public static readonly Error ConcurrentChange = new("tuition_concurrent_change",
        "Tuition data was changed by another request. Reload and try again.");
    public static readonly Error SettingsVersion = new("tuition_concurrent_change",
        "The settings were changed by another request. Reload and try again.", "expectedVersion");
    public static readonly Error TeacherForbidden = new("tuition_forbidden", "Teachers cannot see tuition.");
}
