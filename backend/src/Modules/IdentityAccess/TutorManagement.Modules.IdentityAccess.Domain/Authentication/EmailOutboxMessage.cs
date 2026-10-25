using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class EmailOutboxMessage : Entity<Guid>
{
    private EmailOutboxMessage()
        : base(Guid.Empty)
    {
    }

    private EmailOutboxMessage(
        Guid id,
        string recipient,
        string subject,
        string body,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Recipient = recipient;
        Subject = subject;
        Body = body;
        CreatedAtUtc = createdAtUtc;
    }

    public string Recipient { get; private set; } = string.Empty;

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public Guid? SecurityTokenId { get; private set; }

    public Guid? ProcessingId { get; private set; }

    public DateTimeOffset? ProcessingUntilUtc { get; private set; }

    public static EmailOutboxMessage Create(
        string recipient,
        string subject,
        string body,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new EmailOutboxMessage(
            Guid.NewGuid(),
            recipient.Trim(),
            subject.Trim(),
            body,
            createdAtUtc);
    }

    public static EmailOutboxMessage CreateForSecurityToken(
        string recipient, string subject, string body, Guid securityTokenId, DateTimeOffset createdAtUtc)
    {
        if (securityTokenId == Guid.Empty) throw new ArgumentException("Security token ID is required.", nameof(securityTokenId));
        var message = Create(recipient, subject, body, createdAtUtc);
        message.SecurityTokenId = securityTokenId;
        return message;
    }

    public void MarkProcessed(DateTimeOffset processedAtUtc)
    {
        ProcessedAtUtc = processedAtUtc;
        AttemptCount++;
        LastError = null;
        Body = string.Empty;
        ProcessingId = null;
        ProcessingUntilUtc = null;
    }

    /// <summary>
    /// A failed attempt. <paramref name="retryNotBeforeUtc"/> keeps the row out of the claim query until then
    /// (the claim query skips rows whose <see cref="ProcessingUntilUtc"/> is in the future), so a short SMTP
    /// outage does not burn every attempt within a few polls.
    /// </summary>
    public void MarkFailed(string error, DateTimeOffset? retryNotBeforeUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        AttemptCount++;
        LastError = Trim(error);
        ProcessingId = null;
        ProcessingUntilUtc = retryNotBeforeUtc;
    }

    /// <summary>The provider rejected the message for good (bad address, mailbox does not exist): never retry.</summary>
    public void MarkUndeliverable(string error, int maximumAttempts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        AttemptCount = Math.Max(AttemptCount + 1, maximumAttempts);
        LastError = Trim(error);
        ProcessingId = null;
        ProcessingUntilUtc = null;
    }

    /// <summary>Closed without sending (an expired or superseded code, or attempts used up); the reason stays visible.</summary>
    public void MarkSkipped(string reason, DateTimeOffset processedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ProcessedAtUtc = processedAtUtc;
        LastError = Trim(LastError is null ? reason : $"{reason} {LastError}");
        Body = string.Empty;
        ProcessingId = null;
        ProcessingUntilUtc = null;
    }

    private static string Trim(string value) => value.Length <= 1000 ? value : value[..1000];

    public void Claim(Guid processingId, DateTimeOffset untilUtc)
    {
        if (processingId == Guid.Empty) throw new ArgumentException("Processing ID is required.", nameof(processingId));
        ProcessingId = processingId;
        ProcessingUntilUtc = untilUtc;
    }
}
