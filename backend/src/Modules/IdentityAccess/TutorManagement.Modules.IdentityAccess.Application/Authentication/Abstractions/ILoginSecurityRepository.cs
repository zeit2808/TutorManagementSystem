using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface ILoginSecurityRepository
{
    // Must be called inside a read-committed transaction; serializes attempts for this user.
    Task<LoginSecurityState> AcquireAsync(Guid userId, CancellationToken cancellationToken);
}
