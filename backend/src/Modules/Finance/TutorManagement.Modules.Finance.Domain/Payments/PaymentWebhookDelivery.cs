using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.Finance.Domain.Payments;

/// <summary>
/// A correctly signed webhook: a doorbell that asks for a pull. Never books money and is kept for reconciliation.
/// Webhooks with a bad signature are only counted on the config, so they cannot take a webhook id first.
/// </summary>
public sealed class PaymentWebhookDelivery : Entity<Guid>
{
    public const int MaxReferenceLength = BankTransaction.MaxReferenceLength;

    private PaymentWebhookDelivery() : base(Guid.Empty) { }

    private PaymentWebhookDelivery(Guid configId, long webhookId, string? referenceCode, long? amount, string rawBody,
        DateTimeOffset now) : base(Guid.NewGuid())
    {
        ConfigId = configId;
        WebhookId = webhookId;
        ReferenceCode = referenceCode;
        Amount = amount;
        RawBody = rawBody;
        DeliveryCount = 1;
        FirstSeenAtUtc = now;
        LastSeenAtUtc = now;
    }

    public Guid ConfigId { get; private set; }
    public long WebhookId { get; private set; }
    public string? ReferenceCode { get; private set; }
    public long? Amount { get; private set; }
    public string RawBody { get; private set; } = string.Empty;
    public int DeliveryCount { get; private set; }
    public DateTimeOffset FirstSeenAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public static PaymentWebhookDelivery Create(Guid configId, long webhookId, string? referenceCode, long? amount,
        string rawBody, DateTimeOffset now)
    {
        var reference = string.IsNullOrWhiteSpace(referenceCode) ? null : referenceCode.Trim();
        if (configId == Guid.Empty || string.IsNullOrEmpty(rawBody) || reference?.Length > MaxReferenceLength)
            throw new ArgumentException("Webhook delivery details are invalid.");
        return new(configId, webhookId, reference, amount, rawBody, now);
    }
}
