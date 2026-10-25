using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;
