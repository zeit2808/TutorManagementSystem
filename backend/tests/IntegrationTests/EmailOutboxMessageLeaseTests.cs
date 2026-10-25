using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class EmailOutboxMessageLeaseTests
{
    [Fact]
    public void Successful_delivery_releases_claim_and_removes_sensitive_body()
    {
        var now = DateTimeOffset.UtcNow;
        var message = EmailOutboxMessage.Create("recipient@example.invalid", "Verification", "12345678", now);
        var claimId = Guid.NewGuid();

        message.Claim(claimId, now.AddMinutes(15));
        message.MarkProcessed(now.AddSeconds(1));

        Assert.Null(message.ProcessingId);
        Assert.Null(message.ProcessingUntilUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(string.Empty, message.Body);
        Assert.Equal(now.AddSeconds(1), message.ProcessedAtUtc);
    }

    [Fact]
    public void Failed_delivery_releases_claim_for_retry()
    {
        var now = DateTimeOffset.UtcNow;
        var message = EmailOutboxMessage.Create("recipient@example.invalid", "Verification", "12345678", now);

        message.Claim(Guid.NewGuid(), now.AddMinutes(15));
        message.MarkFailed("Temporary SMTP failure");

        Assert.Null(message.ProcessingId);
        Assert.Null(message.ProcessingUntilUtc);
        Assert.Equal(1, message.AttemptCount);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal("Temporary SMTP failure", message.LastError);
        Assert.Equal("12345678", message.Body);
    }
}
