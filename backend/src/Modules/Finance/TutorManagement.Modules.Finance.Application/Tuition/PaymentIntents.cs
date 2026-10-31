using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>Makes the QR code for what a student owes (docs/THANH-TOAN-QR-KE-HOACH.md). The amount always comes from the ledger.</summary>
public sealed class PaymentIntentWorkflow(ITuitionStore store, ITuitionAcademicsReader reader, IClock clock)
{
    public Task<Result<PaymentIntentDto>> CreateForCenterAsync(Guid centerId, Guid studentId, IReadOnlyList<Guid>? chargeIds,
        Guid actorId, CancellationToken token) => CreateAsync(centerId, studentId, chargeIds, actorId, token);

    /// <summary>A student can only ask for their own profile; an unrelated one looks like it does not exist.</summary>
    public async Task<Result<PaymentIntentDto>> CreateForStudentAsync(Guid userId, Guid studentId,
        IReadOnlyList<Guid>? chargeIds, CancellationToken token)
    {
        var link = (await reader.ListProfilesOfUserAsync(userId, token)).FirstOrDefault(x => x.StudentId == studentId);
        return link is null
            ? Result<PaymentIntentDto>.Failure(TuitionErrors.NotFound)
            : await CreateAsync(link.CenterId, studentId, chargeIds, userId, token);
    }

    private async Task<Result<PaymentIntentDto>> CreateAsync(Guid centerId, Guid studentId, IReadOnlyList<Guid>? chargeIds,
        Guid actorId, CancellationToken token)
    {
        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var ledger = await CenterLedger.LoadAsync(centerId, store, reader, token);
        var now = clock.UtcNow;
        ledger.Sync(FinanceTime.LocalDate(now), now);
        await TuitionWorkflow.PersistAsync(ledger, store, token);

        var student = ledger.FindStudent(studentId);
        if (student is null) return Result<PaymentIntentDto>.Failure(TuitionErrors.NotFound);
        if (student.MergedIntoStudentId.HasValue) return Result<PaymentIntentDto>.Failure(TuitionErrors.StudentUnavailable);
        if (!ledger.Settings.HasBankAccount) return Result<PaymentIntentDto>.Failure(TuitionErrors.BankNotConfigured);

        var open = ledger.OpenCharges(studentId).ToArray();
        var wanted = chargeIds?.Distinct().ToArray() ?? [];
        if (wanted.Any(id => open.All(x => x.Id != id))) return Result<PaymentIntentDto>.Failure(TuitionErrors.InvalidCharges);
        var charges = wanted.Length == 0 ? open : open.Where(x => wanted.Contains(x.Id)).ToArray();
        if (charges.Length == 0) return Result<PaymentIntentDto>.Failure(TuitionErrors.NothingToPay);

        var ids = charges.Select(x => x.Id).ToArray();
        var amount = charges.Sum(ledger.Remaining);
        var pending = await store.ListPendingPaymentIntentsAsync(centerId, studentId, token);
        var intent = pending.FirstOrDefault(x => x.CanBeReusedFor(ids, amount, ledger.Settings, now));
        if (intent is null)
        {
            // An older code for these charges shows a stale amount; its money would still be recognised.
            foreach (var old in pending.Where(x => x.ExpiresAtUtc <= now || x.ChargeIds.Intersect(ids).Any())) old.Cancel(now);
            intent = PaymentIntent.Create(centerId, studentId, PaymentCode.New(), amount, ids, ledger.Settings, actorId, now);
            await store.AddAsync(intent, token);
            try { await store.SaveAsync(token); }
            catch (TuitionPersistenceException) { return Result<PaymentIntentDto>.Failure(TuitionErrors.ConcurrentChange); }
        }
        await guard.CommitAsync(token);
        return Result<PaymentIntentDto>.Success(View(intent));
    }

    private static PaymentIntentDto View(PaymentIntent intent) => new()
    {
        Id = intent.Id, StudentId = intent.StudentId, Code = intent.Code, Amount = intent.Amount,
        TransferContent = intent.TransferContent, QrPayload = intent.QrPayload,
        Bank = new BankAccountDto
        {
            BankBin = intent.BankBin, AccountNumber = intent.AccountNumber, AccountName = intent.AccountName
        },
        ChargeIds = [.. intent.ChargeIds], ExpiresAtUtc = intent.ExpiresAtUtc
    };
}
