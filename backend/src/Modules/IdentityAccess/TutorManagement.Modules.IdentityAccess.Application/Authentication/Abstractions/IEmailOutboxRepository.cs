using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IEmailOutboxRepository
{
    Task AddAsync(EmailOutboxMessage message, CancellationToken cancellationToken);
}
