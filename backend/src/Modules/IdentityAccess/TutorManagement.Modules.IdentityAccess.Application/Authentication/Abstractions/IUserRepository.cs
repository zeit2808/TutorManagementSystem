using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken);

    Task<User?> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<bool> ExistsByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken);

    Task<bool> ExistsByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    Task<bool> ExistsByPhoneNumberAsync(
        string phoneNumber,
        CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
}
