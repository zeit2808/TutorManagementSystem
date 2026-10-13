namespace TutorManagement.Application.Abstractions.Security;

public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? TenantId { get; }

    bool IsAuthenticated { get; }
}
