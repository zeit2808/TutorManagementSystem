namespace TutorManagement.Modules.Finance.Contracts.Payments;

/// <summary>Whose money: a receipt for this student. <c>chargeIds</c> are paid first, then the oldest open charges.</summary>
public sealed record AssignBankTransferRequest(Guid StudentId, int ExpectedVersion, IReadOnlyList<Guid>? ChargeIds = null);

/// <summary>Not tuition (owner or manager). A reason of 3–300 characters is required.</summary>
public sealed record IgnoreBankTransferRequest(string Reason, int ExpectedVersion);

/// <summary>Owner: a held copy is the same money twice (<c>isDuplicate</c>) or a second real transfer. A reason is required.</summary>
public sealed record ResolveHeldTransferRequest(bool IsDuplicate, string Reason, int ExpectedVersion);

/// <summary>Owner: try a failed transfer again after its cause was fixed.</summary>
public sealed record RetryBankTransferRequest(int ExpectedVersion);
