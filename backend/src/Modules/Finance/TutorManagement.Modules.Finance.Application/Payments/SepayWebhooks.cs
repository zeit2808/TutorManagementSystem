using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>The few config fields a webhook needs; read without tracking.</summary>
public sealed record WebhookTarget(Guid ConfigId, Guid CenterId, byte[] WebhookSecret, string KeyId, PaymentProviderMode Mode);

public interface IPaymentWebhookStore
{
    Task<WebhookTarget?> FindByEndpointAsync(PaymentProvider provider, string endpointKey, CancellationToken token);

    /// <summary>Counts a webhook with a bad signature on the config; never stores its body.</summary>
    Task RecordInvalidSignatureAsync(Guid configId, DateTimeOffset now, CancellationToken token);

    /// <summary>
    /// One transaction: stores the doorbell (a repeat only bumps delivery_count) and, unless the config is Off,
    /// asks for a pull now.
    /// </summary>
    Task RecordDeliveryAsync(Guid configId, long webhookId, string? referenceCode, long? amount, string rawBody,
        DateTimeOffset now, CancellationToken token);
}

public enum WebhookOutcome
{
    Accepted = 1,
    NotFound = 2,
    Unauthorized = 3
}

public sealed class WebhookOptions
{
    /// <summary>Allowed clock difference for X-SePay-Timestamp. A doorbell books no money, so this only limits spam.</summary>
    public int TimestampToleranceSeconds { get; init; } = 300;
}

/// <summary>
/// The SePay doorbell (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2d). Verifies HMAC-SHA256 over "{timestamp}.{raw body}",
/// records the delivery and asks the Worker to pull. Nothing in the body is ever used to book money.
/// </summary>
public sealed class SepayWebhookReceiver(IPaymentWebhookStore store, ISecretProtector protector, IClock clock,
    WebhookOptions options)
{
    private const string SignaturePrefix = "sha256=";

    public async Task<WebhookOutcome> ReceiveAsync(string endpointKey, ReadOnlyMemory<byte> rawBody, string? signature,
        string? timestamp, CancellationToken token)
    {
        if (endpointKey.Length != PaymentProviderConfig.EndpointKeyLength ||
            !endpointKey.All(x => char.IsAsciiLetterOrDigit(x) || x is '-' or '_'))
            return WebhookOutcome.NotFound;
        var target = await store.FindByEndpointAsync(PaymentProvider.Sepay, endpointKey, token);
        if (target is null) return WebhookOutcome.NotFound;

        var now = clock.UtcNow;
        if (!IsFresh(timestamp, now) || !SignatureMatches(target, rawBody.Span, signature, timestamp!))
        {
            await store.RecordInvalidSignatureAsync(target.ConfigId, now, token);
            return WebhookOutcome.Unauthorized;
        }

        var body = Encoding.UTF8.GetString(rawBody.Span);
        var (webhookId, reference, amount) = Read(body);
        await store.RecordDeliveryAsync(target.ConfigId, webhookId, reference, amount, body, now, token);
        return WebhookOutcome.Accepted;
    }

    private bool IsFresh(string? timestamp, DateTimeOffset now) =>
        long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
        Math.Abs(now.ToUnixTimeSeconds() - seconds) <= options.TimestampToleranceSeconds;

    private bool SignatureMatches(WebhookTarget target, ReadOnlySpan<byte> rawBody, string? signature, string timestamp)
    {
        if (signature is null || !signature.StartsWith(SignaturePrefix, StringComparison.Ordinal)) return false;
        byte[] given;
        try { given = Convert.FromHexString(signature.AsSpan(SignaturePrefix.Length).Trim()); }
        catch (FormatException) { return false; }
        if (given.Length != SHA256.HashSizeInBytes) return false;

        var secret = Encoding.UTF8.GetBytes(protector.Unprotect(target.WebhookSecret, target.KeyId,
            SecretPurposes.WebhookSecret(target.CenterId, nameof(PaymentProvider.Sepay))));
        try
        {
            var signed = new byte[timestamp.Length + 1 + rawBody.Length];
            Encoding.ASCII.GetBytes(timestamp, signed);
            signed[timestamp.Length] = (byte)'.';
            rawBody.CopyTo(signed.AsSpan(timestamp.Length + 1));
            return CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(secret, signed), given);
        }
        finally { CryptographicOperations.ZeroMemory(secret); }
    }

    /// <summary>Only for the delivery log and reconciliation; a body that cannot be read is still a doorbell.</summary>
    private static (long WebhookId, string? Reference, long? Amount) Read(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (0, null, null);
            var id = root.TryGetProperty("id", out var x) && x.ValueKind == JsonValueKind.Number && x.TryGetInt64(out var n) ? n : 0;
            var reference = root.TryGetProperty("referenceCode", out var r) && r.ValueKind == JsonValueKind.String
                ? Trim(r.GetString(), PaymentWebhookDelivery.MaxReferenceLength)
                : null;
            long? amount = root.TryGetProperty("transferAmount", out var a) && a.ValueKind == JsonValueKind.Number &&
                           a.TryGetInt64(out var v) ? v : null;
            return (id, reference, amount);
        }
        catch (JsonException) { return (0, null, null); }
    }

    private static string? Trim(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }
}
