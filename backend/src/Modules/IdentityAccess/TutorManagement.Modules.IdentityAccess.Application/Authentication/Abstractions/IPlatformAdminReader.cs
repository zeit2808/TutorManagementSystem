namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IPlatformAdminReader
{
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);
}
