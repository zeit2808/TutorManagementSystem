using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Tuition;

public sealed record TuitionClassRow(Guid Id, string Code, string Name, bool IsArchived);

public sealed record TuitionSessionRow(Guid ClassId, DateTimeOffset StartUtc);

public sealed record TuitionEnrollmentRow(Guid Id, Guid ClassId, Guid StudentId, DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? WithdrawnAtUtc);

public sealed record TuitionStudentRow(Guid Id, string Code, string FullName, Guid? UserId, Guid? MergedIntoStudentId,
    string? ParentName, string? ParentPhone, bool IsArchived);

/// <summary>A session of the class that did not take place (cancelled or marked not held).</summary>
public sealed record TuitionMissedSessionRow(Guid ClassId, Guid SessionId, DateTimeOffset StartUtc, string Kind,
    string? Reason, bool HasMakeup);

/// <summary>A student marked absent on a submitted attendance sheet.</summary>
public sealed record TuitionAbsenceRow(Guid ClassId, Guid SessionId, DateTimeOffset StartUtc, Guid EnrollmentId, string Mark);

public sealed record TuitionStudentLink(Guid CenterId, Guid StudentId);

/// <summary>Read-only view of the Academics data tuition is computed from (classes, sessions, enrollments, students).</summary>
public interface ITuitionAcademicsReader
{
    Task<IReadOnlyList<TuitionClassRow>> ListClassesAsync(Guid centerId, CancellationToken token);

    /// <summary>Sessions that take place: neither cancelled nor marked not held.</summary>
    Task<IReadOnlyList<TuitionSessionRow>> ListSessionsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token);

    Task<IReadOnlyList<TuitionMissedSessionRow>> ListMissedSessionsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token);

    /// <summary>ExcusedAbsent / UnexcusedAbsent marks of submitted sheets.</summary>
    Task<IReadOnlyList<TuitionAbsenceRow>> ListAbsencesAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token);

    Task<IReadOnlyList<TuitionEnrollmentRow>> ListEnrollmentsAsync(Guid centerId, IReadOnlyCollection<Guid> classIds,
        CancellationToken token);

    Task<IReadOnlyList<TuitionStudentRow>> ListStudentsAsync(Guid centerId, CancellationToken token);

    /// <summary>The student profiles a login is linked to, in every center.</summary>
    Task<IReadOnlyList<TuitionStudentLink>> ListProfilesOfUserAsync(Guid userId, CancellationToken token);

    Task<IReadOnlyDictionary<Guid, string>> ReadCenterNamesAsync(IReadOnlyCollection<Guid> centerIds, CancellationToken token);

    Task<bool> ClassExistsAsync(Guid centerId, Guid classId, CancellationToken token);
}

public interface ITuitionGuard : IAsyncDisposable
{
    Task CommitAsync(CancellationToken token);
}

public sealed record OutgoingTuitionEmail(string Recipient, string Subject, string Body);

public interface ITuitionStore
{
    /// <summary>One writer per center at a time (sync, payments, discounts) inside a transaction.</summary>
    Task<ITuitionGuard> BeginCenterGuardAsync(Guid centerId, CancellationToken token);

    /// <summary>
    /// A read-only, consistent view of the center for screens: no lock, so readers never queue behind each other
    /// or behind a writer. Disposing it without committing is the normal way to end it.
    /// </summary>
    Task<ITuitionGuard> BeginSnapshotAsync(CancellationToken token);

    /// <summary>True when a loaded entity was modified or an entity was added since the last save.</summary>
    bool HasChanges();

    Task<IReadOnlyList<ClassFee>> ListFeesAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<ClassBillingAnchor>> ListAnchorsAsync(Guid centerId, CancellationToken token);
    /// <summary>Tracked: every charge of the center.</summary>
    Task<IReadOnlyList<TuitionCharge>> ListChargesAsync(Guid centerId, CancellationToken token);
    /// <summary>Tracked: every discount of the center.</summary>
    Task<IReadOnlyList<TuitionDiscount>> ListDiscountsAsync(Guid centerId, CancellationToken token);
    /// <summary>Tracked, with allocations: every receipt of the center.</summary>
    Task<IReadOnlyList<Receipt>> ListReceiptsAsync(Guid centerId, CancellationToken token);
    Task<FinanceSettings?> FindSettingsAsync(Guid centerId, CancellationToken token);
    Task<int> NextReceiptNumberAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<Guid>> ListCentersWithFeesAsync(CancellationToken token);
    /// <summary>Tracked: the student's QR codes that have not been cancelled.</summary>
    Task<IReadOnlyList<PaymentIntent>> ListPendingPaymentIntentsAsync(Guid centerId, Guid studentId, CancellationToken token);
    /// <summary>Tracked: the center's connection to a bank feed, if any.</summary>
    Task<Domain.Payments.PaymentProviderConfig?> FindProviderConfigAsync(Guid centerId, Domain.Payments.PaymentProvider provider,
        CancellationToken token);

    Task AddAsync<T>(T entity, CancellationToken token) where T : class;
    Task SaveAsync(CancellationToken token);

    /// <summary>Forgets every tracked entity, so a failed or finished unit of work cannot leak into the next one.</summary>
    void Detach();

    /// <summary>Untracked: the center's pulled transfers in [fromUtc, toUtc).</summary>
    Task<IReadOnlyList<Domain.Payments.BankTransaction>> ListBankTransactionsAsync(Guid centerId, DateTimeOffset fromUtc,
        DateTimeOffset toUtc, CancellationToken token);

    /// <summary>Untracked page of the center's transfers, newest first; <paramref name="statuses"/> null means every status.</summary>
    Task<(IReadOnlyList<Domain.Payments.BankTransaction> Items, int Total)> ListBankTransfersPageAsync(Guid centerId,
        IReadOnlyCollection<Domain.Payments.BankTransactionStatus>? statuses, int page, int pageSize, CancellationToken token);

    /// <summary>Untracked: QR codes of the center still waiting for money.</summary>
    Task<IReadOnlyList<PaymentIntent>> ListPendingIntentsOfCenterAsync(Guid centerId, CancellationToken token);

    /// <summary>The live receipt (id, number) of each transfer that has one.</summary>
    Task<IReadOnlyDictionary<Guid, (Guid Id, int Number)>> ListLiveReceiptsOfTransfersAsync(IReadOnlyCollection<Guid> transferIds,
        CancellationToken token);

    /// <summary>Tracked: the bank transfer a receipt was made from.</summary>
    Task<Domain.Payments.BankTransaction?> FindBankTransactionAsync(Guid id, CancellationToken token);

    Task QueueEmailsAsync(IReadOnlyList<OutgoingTuitionEmail> emails, CancellationToken token);
    Task<IReadOnlyDictionary<Guid, string>> ReadEmailsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken token);
    string? StudentPortalUrl { get; }
}

/// <summary>A receipt number or request id was used twice (unique index).</summary>
public sealed class TuitionPersistenceException(string? constraintName, Exception inner)
    : Exception("Tuition data changed concurrently.", inner)
{
    public string? ConstraintName { get; } = constraintName;
}
