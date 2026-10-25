namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public interface IEmailDeliveryClient
{
    Task DeliverAsync(
        EmailDeliveryRequest request,
        CancellationToken cancellationToken);
}
