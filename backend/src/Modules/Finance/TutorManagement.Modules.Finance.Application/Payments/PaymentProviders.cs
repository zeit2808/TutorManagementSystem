using System.Globalization;
using System.Text.Json;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>What the owner and managers see about the bank feed. Secrets are never returned, only whether they are set.</summary>
public sealed class PaymentProviderDto
{
    public bool Configured { get; init; }
    public string Provider { get; init; } = nameof(PaymentProvider.Sepay);
    public string Mode { get; init; } = nameof(PaymentProviderMode.Off);
    public string? BankBin { get; init; }
    public string? AccountNumber { get; init; }

    /// <summary>False when the tuition bank account changed after the feed was set up: save again to follow it.</summary>
    public bool AccountMatchesSettings { get; init; }

    /// <summary>Path to paste into SePay as the webhook URL, after the public API address.</summary>
    public string? WebhookPath { get; init; }

    public bool HasWebhookSecret { get; init; }
    public bool HasApiToken { get; init; }
    public long InvalidSignatureCount { get; init; }
    public DateTimeOffset? LastInvalidSignatureAtUtc { get; init; }
    public DateTimeOffset? LastPulledAtUtc { get; init; }
    public string? LastPullError { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public int Version { get; init; }
}

/// <summary>The caller's request context, recorded in the security audit when the password is checked.</summary>
public sealed record RequestOrigin(string? ClientIp, string? UserAgent, string? TraceId);

public static class PaymentErrors
{
    public const int MinSecretLength = 16;
    public const int MaxSecretLength = 256;
    public const int MaxTokenLength = 512;

    public static readonly Error InvalidMode = new("validation_error", "mode must be Off or Shadow.", "mode");
    public static readonly Error AutoNotAvailable = new("payment_auto_not_available",
        "Automatic booking is not available yet; use Shadow to collect transactions.", "mode");
    public static readonly Error InvalidWebhookSecret = new("validation_error",
        $"webhookSecret must be {MinSecretLength}–{MaxSecretLength} characters without spaces.", "webhookSecret");
    public static readonly Error InvalidApiToken = new("validation_error",
        $"apiToken must be {MinSecretLength}–{MaxTokenLength} characters without spaces.", "apiToken");
    public static readonly Error SecretsRequired = new("validation_error",
        "webhookSecret and apiToken are required when connecting for the first time.", "webhookSecret");
    public static readonly Error InvalidPassword = new("payment_invalid_password",
        "The password is not correct, or too many attempts were made. Try again later.", "currentPassword");
    public static readonly Error PasswordRequired = new("payment_password_required",
        "This account signs in with Google only. Set a password (Forgot password) before changing payment settings.",
        "currentPassword");
    public static readonly Error TokenInUse = new("payment_token_in_use",
        "This SePay API token is already connected to another center.", "apiToken");
    public static readonly Error ConfigVersion = new("tuition_concurrent_change",
        "The payment settings were changed by another request. Reload and try again.", "expectedVersion");
}

/// <summary>
/// The owner connects the center's SePay account (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2c, D7):
/// password asked again, every change audited and emailed to all owners, secrets stored encrypted and never shown.
/// Only Off and Shadow can be chosen until booking exists (Round 3).
/// </summary>
public sealed class PaymentProviderWorkflow(
    ITuitionStore store,
    ISecretProtector protector,
    IPasswordReauthenticator reauthenticator,
    ICenterMemberDirectory members,
    IClock clock)
{
    private const PaymentProvider Provider = PaymentProvider.Sepay;
    private static readonly string ProviderName = Provider.ToString();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<PaymentProviderDto> GetAsync(Guid centerId, CancellationToken token)
    {
        var config = await store.FindProviderConfigAsync(centerId, Provider, token);
        var settings = await store.FindSettingsAsync(centerId, token);
        return View(config, settings);
    }

    public async Task<Result<PaymentProviderDto>> SaveAsync(Guid centerId, Guid actorId, string currentPassword, string? mode,
        string? webhookSecret, string? apiToken, int expectedVersion, RequestOrigin origin, CancellationToken token)
    {
        PaymentProviderMode wanted;
        switch (mode)
        {
            case nameof(PaymentProviderMode.Off): wanted = PaymentProviderMode.Off; break;
            case nameof(PaymentProviderMode.Shadow): wanted = PaymentProviderMode.Shadow; break;
            case nameof(PaymentProviderMode.Auto): wanted = PaymentProviderMode.Auto; break;
            default: return Result<PaymentProviderDto>.Failure(PaymentErrors.InvalidMode);
        }
        if (wanted == PaymentProviderMode.Auto) return Result<PaymentProviderDto>.Failure(PaymentErrors.AutoNotAvailable);
        var secret = Clean(webhookSecret);
        var apiKey = Clean(apiToken);
        if (secret is not null && !IsValid(secret, PaymentErrors.MaxSecretLength))
            return Result<PaymentProviderDto>.Failure(PaymentErrors.InvalidWebhookSecret);
        if (apiKey is not null && !IsValid(apiKey, PaymentErrors.MaxTokenLength))
            return Result<PaymentProviderDto>.Failure(PaymentErrors.InvalidApiToken);

        // Checked before anything is read or locked, so a wrong password reveals nothing about the settings.
        switch (await reauthenticator.VerifyAsync(actorId, currentPassword, origin.ClientIp, origin.UserAgent, origin.TraceId,
                    token))
        {
            case ReauthenticationResult.Verified: break;
            case ReauthenticationResult.NoLocalPassword: return Result<PaymentProviderDto>.Failure(PaymentErrors.PasswordRequired);
            default: return Result<PaymentProviderDto>.Failure(PaymentErrors.InvalidPassword);
        }

        await using var guard = await store.BeginCenterGuardAsync(centerId, token);
        var settings = await store.FindSettingsAsync(centerId, token);
        if (settings is null || !settings.HasBankAccount) return Result<PaymentProviderDto>.Failure(TuitionErrors.BankNotConfigured);
        var config = await store.FindProviderConfigAsync(centerId, Provider, token);
        if ((config?.Version ?? 0) != expectedVersion) return Result<PaymentProviderDto>.Failure(PaymentErrors.ConfigVersion);

        var now = clock.UtcNow;
        var changes = new List<string>();
        string? before = null;
        if (config is null)
        {
            if (secret is null || apiKey is null) return Result<PaymentProviderDto>.Failure(PaymentErrors.SecretsRequired);
            config = PaymentProviderConfig.Create(centerId, Provider, settings.BankBin!, settings.BankAccountNumber!,
                Protect(centerId, secret, apiKey), actorId, now);
            config.ChangeMode(wanted, actorId, now);
            await store.AddAsync(config, token);
            changes.Add("Kết nối SePay lần đầu");
            changes.Add($"Chế độ: {wanted}");
        }
        else
        {
            before = Snapshot(config);
            if (secret is not null || apiKey is not null || config.KeyId != protector.ActiveKeyId)
            {
                var keepSecret = secret ?? protector.Unprotect(config.WebhookSecret, config.KeyId,
                    SecretPurposes.WebhookSecret(centerId, ProviderName));
                var keepToken = apiKey ?? protector.Unprotect(config.ApiToken, config.KeyId,
                    SecretPurposes.ApiToken(centerId, ProviderName));
                config.ReplaceSecrets(Protect(centerId, keepSecret, keepToken), actorId, now);
                if (secret is not null) changes.Add("Đổi Secret Key webhook");
                if (apiKey is not null) changes.Add("Đổi API token");
            }
            if (config.BankBin != settings.BankBin || config.AccountNumber != settings.BankAccountNumber)
            {
                changes.Add($"Tài khoản nhận: {Mask(config.AccountNumber)} → {Mask(settings.BankAccountNumber!)}");
                config.ChangeAccount(settings.BankBin!, settings.BankAccountNumber!, actorId, now);
            }
            if (config.Mode != wanted)
            {
                changes.Add($"Chế độ: {config.Mode} → {wanted}");
                config.ChangeMode(wanted, actorId, now);
            }
            if (changes.Count == 0)
            {
                await guard.CommitAsync(token);
                return Result<PaymentProviderDto>.Success(View(config, settings));
            }
        }

        await store.AddAsync(PaymentAuditEvent.Record(centerId, actorId,
            before is null ? "provider_connected" : "provider_changed", "payment_provider_config", config.Id,
            before, Snapshot(config), now), token);
        await QueueAlertsAsync(centerId, actorId, changes, now, token);
        try { await store.SaveAsync(token); }
        catch (TuitionPersistenceException error) when (error.ConstraintName == "ux_payment_provider_configs_token")
        { return Result<PaymentProviderDto>.Failure(PaymentErrors.TokenInUse); }
        catch (TuitionPersistenceException)
        { return Result<PaymentProviderDto>.Failure(PaymentErrors.ConfigVersion); }
        await guard.CommitAsync(token);
        return Result<PaymentProviderDto>.Success(View(config, settings));
    }

    private ProtectedSecrets Protect(Guid centerId, string secret, string apiToken) => new(
        protector.Protect(secret, SecretPurposes.WebhookSecret(centerId, ProviderName)),
        protector.Protect(apiToken, SecretPurposes.ApiToken(centerId, ProviderName)),
        protector.Fingerprint(apiToken, SecretPurposes.ApiTokenFingerprint(ProviderName)),
        protector.ActiveKeyId);

    /// <summary>Every active owner hears about the change, so a stolen session cannot redirect money quietly.</summary>
    private async Task QueueAlertsAsync(Guid centerId, Guid actorId, IReadOnlyList<string> changes, DateTimeOffset now,
        CancellationToken token)
    {
        var owners = await members.ListActiveUserIdsAsync(centerId, ["CenterOwner"], token);
        var emails = await store.ReadEmailsAsync([.. owners, actorId], token);
        if (owners.Count == 0) return;
        var actor = emails.TryGetValue(actorId, out var actorEmail) ? actorEmail : actorId.ToString();
        var at = TimeZoneInfo.ConvertTime(now, FinanceTime.Zone).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);
        var body = $"""
            Cấu hình nhận tiền qua SePay của trung tâm vừa được thay đổi.

            Thời điểm: {at} (giờ Việt Nam)
            Người thay đổi: {actor}
            Nội dung:
            {string.Join('\n', changes.Select(x => "- " + x))}

            Nếu bạn không thực hiện thay đổi này: đổi mật khẩu ngay, kiểm tra lại cấu hình thanh toán
            và tạo lại API token, Secret Key trên my.sepay.vn.
            """;
        var outgoing = owners.Where(emails.ContainsKey)
            .Select(x => new OutgoingTuitionEmail(emails[x], "Cấu hình thanh toán SePay vừa thay đổi", body)).ToArray();
        await store.QueueEmailsAsync(outgoing, token);
    }

    private static PaymentProviderDto View(PaymentProviderConfig? config, FinanceSettings? settings) =>
        config is null
            ? new PaymentProviderDto { AccountMatchesSettings = false }
            : new PaymentProviderDto
            {
                Configured = true,
                Provider = config.Provider.ToString(),
                Mode = config.Mode.ToString(),
                BankBin = config.BankBin,
                AccountNumber = config.AccountNumber,
                AccountMatchesSettings = settings is not null && settings.BankBin == config.BankBin &&
                                         settings.BankAccountNumber == config.AccountNumber,
                WebhookPath = $"/api/v1/payments/webhooks/sepay/{config.EndpointKey}",
                HasWebhookSecret = config.WebhookSecret.Length > 0,
                HasApiToken = config.ApiToken.Length > 0,
                InvalidSignatureCount = config.InvalidSignatureCount,
                LastInvalidSignatureAtUtc = config.LastInvalidSignatureAtUtc,
                LastPulledAtUtc = config.LastPulledAtUtc,
                LastPullError = config.LastPullError,
                UpdatedAtUtc = config.UpdatedAtUtc,
                Version = config.Version
            };

    /// <summary>Audit snapshot: what changed, never a secret (the fingerprint shows the token changed without revealing it).</summary>
    private static string Snapshot(PaymentProviderConfig config) => JsonSerializer.Serialize(new
    {
        mode = config.Mode.ToString(),
        bankBin = config.BankBin,
        accountNumber = config.AccountNumber,
        keyId = config.KeyId,
        apiTokenFingerprint = config.ApiTokenFingerprint[..12]
    }, Json);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValid(string value, int max) =>
        value.Length >= PaymentErrors.MinSecretLength && value.Length <= max && !value.Any(char.IsWhiteSpace) &&
        !value.Any(char.IsControl);

    private static string Mask(string account) => account.Length <= 4 ? account : new string('*', account.Length - 4) + account[^4..];
}
