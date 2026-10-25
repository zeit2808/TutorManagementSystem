using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed record ChangePasswordCommand(
    Guid UserId, Guid SessionId, string RefreshToken, string CurrentPassword, string NewPassword)
    : ICommand<AuthenticationResult>;
