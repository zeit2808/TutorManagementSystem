using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

public enum PaymentIntentStatus
{
    Pending = 1,
    Cancelled = 2
}

/// <summary>
/// One QR code offered to one student: the charges it pays, the amount and the bank account, frozen when it was made.
/// The code stays traceable after expiry or cancellation: money that arrives late still belongs to the student.
/// </summary>
public sealed class PaymentIntent : Entity<Guid>
{
    public const int LifetimeDays = 7;

    private PaymentIntent() : base(Guid.Empty) { }

    private PaymentIntent(Guid centerId, Guid studentId, string code, long amount, Guid[] chargeIds, string bankBin,
        string accountNumber, string accountName, Guid createdByUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        StudentId = studentId;
        Code = code;
        Amount = amount;
        ChargeIds = chargeIds;
        BankBin = bankBin;
        AccountNumber = accountNumber;
        AccountName = accountName;
        Status = PaymentIntentStatus.Pending;
        ExpiresAtUtc = now.AddDays(LifetimeDays);
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public Guid StudentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public long Amount { get; private set; }
    public Guid[] ChargeIds { get; private set; } = [];
    public string BankBin { get; private set; } = string.Empty;
    public string AccountNumber { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public PaymentIntentStatus Status { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public string TransferContent => $"{Code} HOC PHI";
    public string QrPayload => VietQr.Build(BankBin, AccountNumber, Amount, TransferContent);

    public static PaymentIntent Create(Guid centerId, Guid studentId, string code, long amount, IEnumerable<Guid> chargeIds,
        FinanceSettings settings, Guid createdByUserId, DateTimeOffset now)
    {
        var charges = chargeIds.Distinct().Order().ToArray();
        if (centerId == Guid.Empty || studentId == Guid.Empty || createdByUserId == Guid.Empty || amount <= 0 ||
            charges.Length == 0 || !PaymentCode.IsValid(code) || !settings.HasBankAccount)
            throw new ArgumentException("Payment intent details are invalid.");
        return new(centerId, studentId, code, amount, charges, settings.BankBin!, settings.BankAccountNumber!,
            settings.BankAccountName!, createdByUserId, now);
    }

    /// <summary>Asking again for the same charges at the same amount and account shows the same code.</summary>
    public bool CanBeReusedFor(IEnumerable<Guid> chargeIds, long amount, FinanceSettings settings, DateTimeOffset now) =>
        Status == PaymentIntentStatus.Pending && ExpiresAtUtc > now && Amount == amount &&
        BankBin == settings.BankBin && AccountNumber == settings.BankAccountNumber &&
        AccountName == settings.BankAccountName && ChargeIds.SequenceEqual(chargeIds.Distinct().Order());

    public void Cancel(DateTimeOffset now)
    {
        if (Status != PaymentIntentStatus.Pending) return;
        Status = PaymentIntentStatus.Cancelled;
        CancelledAtUtc = now;
    }
}
