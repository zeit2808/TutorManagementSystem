using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Email;

/// <summary>One outbox row as the platform admin sees it. Bodies are never shown (they may hold codes).</summary>
public sealed class OutboxEmailDto
{
    public Guid Id { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    /// <summary>Pending (not tried yet), Retrying (failed, will try again), Sent, Skipped (closed without sending), Failed (gave up).</summary>
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}

public sealed class EmailOverviewDto
{
    public int Pending { get; set; }
    public int Retrying { get; set; }
    public int Failed { get; set; }
    public int SentLast24Hours { get; set; }
    public int MaximumAttempts { get; set; }
    public List<OutboxEmailDto> Recent { get; set; } = [];
}

public static class PlatformEmailErrors
{
    public static readonly Error InvalidRecipient = new("validation_error", "Provide a valid email address.", "to");
    public static readonly Error NoOwnEmail = new("validation_error", "Your account has no email; enter a recipient.", "to");
    public static readonly Error TooSoon = new("email_test_too_soon", "A test email was queued less than 30 seconds ago.");
}

/// <summary>
/// Platform admin tools to check real email delivery: queue a test message through the same outbox the
/// Worker sends from, and see what happened to the latest messages.
/// </summary>
public interface IPlatformEmailService
{
    Task<EmailOverviewDto> OverviewAsync(CancellationToken token);
    Task<Result<OutboxEmailDto>> QueueTestAsync(Guid actorId, string? to, CancellationToken token);
}
