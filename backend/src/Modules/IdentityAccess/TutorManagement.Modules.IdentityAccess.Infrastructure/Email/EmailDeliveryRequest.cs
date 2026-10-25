namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed record EmailDeliveryRequest(
    Guid MessageId,
    string Recipient,
    string Subject,
    string Body,
    DateTimeOffset CreatedAtUtc);
