using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthenticationResult>;
