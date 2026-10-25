using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

public sealed class EmailOutboxRepository(IdentityDbContext dbContext) : IEmailOutboxRepository
{
    public async Task AddAsync(
        EmailOutboxMessage message,
        CancellationToken cancellationToken) =>
        await dbContext.EmailOutboxMessages.AddAsync(message, cancellationToken);
}
