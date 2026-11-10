using System.Text.Json;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Tuition;

/// <summary>
/// Tuition settings. Due days and reminders: owner or manager. The bank account decides where every QR sends money (D7),
/// so changing it needs the owner, the password again, an audit entry and an email to every owner.
/// A connected bank feed follows the new account in the same transaction.
/// </summary>
public sealed class FinanceSettingsWorkflow(
    ITuitionStore store,
    IPasswordReauthenticator reauthenticator,
    ICenterMemberDirectory members,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<FinanceSettingsDto>> UpdateAsync(Guid centerId, Guid actorId, bool actorIsOwner, string? bankBin,
        string? accountNumber, string? accountName, int dueDays, bool emailReminders, int expectedVersion,
        string? currentPassword, RequestOrigin origin, CancellationToken token)
    {
        if (!FinanceSettings.IsValid(bankBin, accountNumber, accountName, dueDays))
            return Result<FinanceSettingsDto>.Failure(TuitionErrors.InvalidSettings);

        // Decided before locking so the password is checked outside the transaction; the version check below
        // refuses the save if someone else changed the account in between.
        var current = await store.FindSettingsAsync(centerId, token);
        var bankChanged = BankChanged(current, bankBin, accountNumber, accountName);
        if (bankChanged)
        {
            if (!actorIsOwner) return Result<FinanceSettingsDto>.Failure(TuitionErrors.BankChangeOwnerOnly);
            var rejected = OwnerAlerts.Reject(await reauthenticator.VerifyAsync(actorId, currentPassword ?? string.Empty,
                origin.ClientIp, origin.UserAgent, origin.TraceId, token));
            if (rejected is not null) return Result<FinanceSettingsDto>.Failure(rejected);
        }

        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var settings = await store.FindSettingsAsync(centerId, token);
        if ((settings?.Version ?? 0) != expectedVersion) return Result<FinanceSettingsDto>.Failure(TuitionErrors.SettingsVersion);
        if (BankChanged(settings, bankBin, accountNumber, accountName) != bankChanged)
            return Result<FinanceSettingsDto>.Failure(TuitionErrors.SettingsVersion);
        if (settings is null)
        {
            settings = FinanceSettings.Default(centerId);
            await store.AddAsync(settings, token);
        }

        var now = clock.UtcNow;
        var before = Snapshot(settings);
        var oldAccount = settings.BankAccountNumber;
        settings.Update(bankBin, accountNumber, accountName, dueDays, emailReminders, actorId, now);
        if (bankChanged)
        {
            var changes = new List<string>
            {
                $"Tài khoản nhận học phí: {OwnerAlerts.Mask(oldAccount)} → {OwnerAlerts.Mask(settings.BankAccountNumber)}",
                $"Tên chủ tài khoản: {settings.BankAccountName ?? "(không có)"}"
            };
            var feed = await store.FindProviderConfigAsync(centerId, PaymentProvider.Sepay, token);
            if (feed is not null)
            {
                if (settings.HasBankAccount) feed.ChangeAccount(settings.BankBin!, settings.BankAccountNumber!, actorId, now);
                else if (feed.Mode != PaymentProviderMode.Off)
                {
                    feed.ChangeMode(PaymentProviderMode.Off, actorId, now);
                    changes.Add("SePay chuyển sang Off vì không còn tài khoản nhận");
                }
            }
            await store.AddAsync(PaymentAuditEvent.Record(centerId, actorId, "bank_account_changed", "finance_settings",
                null, before, Snapshot(settings), now), token);
            await OwnerAlerts.QueueAsync(store, members, centerId, actorId, "Tài khoản nhận học phí vừa thay đổi",
                "Tài khoản ngân hàng nhận học phí của trung tâm vừa được thay đổi. Mã QR tạo từ bây giờ sẽ chuyển tiền vào tài khoản mới.",
                changes, "đổi mật khẩu ngay và sửa lại tài khoản ngân hàng trong Cài đặt học phí.", now, token);
        }

        try { await store.SaveAsync(token); }
        catch (TuitionPersistenceException) { return Result<FinanceSettingsDto>.Failure(TuitionErrors.SettingsVersion); }
        await guard.CommitAsync(token);
        return Result<FinanceSettingsDto>.Success(TuitionWorkflow.Settings(settings));
    }

    /// <summary>Compares the way <see cref="FinanceSettings.Update"/> stores the values (blank is none, name upper case).</summary>
    private static bool BankChanged(FinanceSettings? settings, string? bankBin, string? accountNumber, string? accountName) =>
        Blank(settings?.BankBin) != Blank(bankBin) || Blank(settings?.BankAccountNumber) != Blank(accountNumber) ||
        Blank(settings?.BankAccountName)?.ToUpperInvariant() != Blank(accountName)?.ToUpperInvariant();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Snapshot(FinanceSettings x) => JsonSerializer.Serialize(new
    {
        bankBin = x.BankBin, bankAccountNumber = x.BankAccountNumber, bankAccountName = x.BankAccountName
    }, Json);
}
