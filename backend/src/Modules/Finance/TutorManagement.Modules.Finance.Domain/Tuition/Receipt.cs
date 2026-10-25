using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2
}

/// <summary>
/// H10–H12: money received from one student, numbered per center. The amount is spread over that student's charges;
/// whatever is not spread is the student's credit and is spread over charges billed later. Voiding never deletes.
/// </summary>
public sealed class Receipt : Entity<Guid>
{
    public const int MaxNoteLength = 300;
    public const int MaxPayerLength = 150;
    private readonly List<ReceiptAllocation> _allocations = [];

    private Receipt() : base(Guid.Empty) { }

    private Receipt(Guid centerId, int number, Guid studentId, string? payerName, PaymentMethod method, long amount,
        DateOnly receivedOn, string? note, Guid? requestId, Guid createdByUserId, DateTimeOffset now) : base(Guid.NewGuid())
    {
        CenterId = centerId;
        Number = number;
        StudentId = studentId;
        PayerName = payerName;
        Method = method;
        Amount = amount;
        ReceivedOn = receivedOn;
        Note = note;
        RequestId = requestId;
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = now;
    }

    public Guid CenterId { get; private set; }
    public int Number { get; private set; }
    public Guid StudentId { get; private set; }
    public string? PayerName { get; private set; }
    public PaymentMethod Method { get; private set; }
    public long Amount { get; private set; }
    public DateOnly ReceivedOn { get; private set; }
    public string? Note { get; private set; }
    /// <summary>Client-chosen id so a double click records one receipt.</summary>
    public Guid? RequestId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? VoidedAtUtc { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyList<ReceiptAllocation> Allocations => _allocations;

    public bool IsVoided => VoidedAtUtc is not null;
    public long Allocated => _allocations.Sum(x => x.Amount);
    /// <summary>Credit left on this receipt.</summary>
    public long Unallocated => IsVoided ? 0 : Amount - Allocated;

    public static string FormatNumber(int number) => $"PT-{number:000000}";

    public static bool IsValidText(string? value, int max) => value is null || value.Trim().Length <= max;

    public static Receipt Create(Guid centerId, int number, Guid studentId, string? payerName, PaymentMethod method,
        long amount, DateOnly receivedOn, string? note, Guid? requestId, Guid createdByUserId, DateTimeOffset now)
    {
        if (centerId == Guid.Empty || studentId == Guid.Empty || createdByUserId == Guid.Empty || number <= 0 ||
            amount <= 0 || amount > 10 * TuitionMath.MaxFee || !Enum.IsDefined(method) ||
            !IsValidText(payerName, MaxPayerLength) || !IsValidText(note, MaxNoteLength))
            throw new ArgumentException("Receipt details are invalid.");
        return new(centerId, number, studentId, Blank(payerName), method, amount, receivedOn, Blank(note), requestId,
            createdByUserId, now);
    }

    /// <summary>Spreads up to <paramref name="wanted"/> over one charge; returns what was actually spread.</summary>
    public long Allocate(Guid chargeId, long wanted, DateTimeOffset now)
    {
        if (IsVoided || wanted <= 0) return 0;
        var amount = Math.Min(wanted, Unallocated);
        if (amount <= 0) return 0;
        var existing = _allocations.FirstOrDefault(x => x.ChargeId == chargeId);
        if (existing is not null) existing.Add(amount);
        else _allocations.Add(new ReceiptAllocation(Id, chargeId, amount, now));
        Version++;
        return amount;
    }

    /// <summary>Gives back up to <paramref name="wanted"/> from one charge to this receipt's credit.</summary>
    public long Release(Guid chargeId, long wanted)
    {
        var existing = _allocations.FirstOrDefault(x => x.ChargeId == chargeId);
        if (existing is null || wanted <= 0) return 0;
        var amount = Math.Min(wanted, existing.Amount);
        existing.Add(-amount);
        if (existing.Amount == 0) _allocations.Remove(existing);
        Version++;
        return amount;
    }

    public void Void(string reason, Guid actorId, DateTimeOffset now)
    {
        if (IsVoided) throw new InvalidOperationException("The receipt is already void.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 3 or > MaxNoteLength)
            throw new ArgumentException("A reason is required.");
        VoidedAtUtc = now;
        VoidedByUserId = actorId;
        VoidReason = reason.Trim();
        Version++;
    }

    /// <summary>The profile was merged into another one (M3); the money follows.</summary>
    public void MoveToStudent(Guid studentId)
    {
        if (studentId == Guid.Empty || studentId == StudentId) return;
        StudentId = studentId;
        Version++;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class ReceiptAllocation : Entity<Guid>
{
    private ReceiptAllocation() : base(Guid.Empty) { }

    internal ReceiptAllocation(Guid receiptId, Guid chargeId, long amount, DateTimeOffset now) : base(Guid.NewGuid())
    {
        ReceiptId = receiptId;
        ChargeId = chargeId;
        Amount = amount;
        CreatedAtUtc = now;
    }

    public Guid ReceiptId { get; private set; }
    public Guid ChargeId { get; private set; }
    public long Amount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal void Add(long amount) => Amount += amount;
}
