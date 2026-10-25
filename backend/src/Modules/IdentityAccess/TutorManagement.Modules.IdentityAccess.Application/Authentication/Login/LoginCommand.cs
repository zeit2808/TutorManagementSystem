using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;

public sealed record LoginCommand(
    string Username,
    string Password,
    string? ClientIp = null,
    string? UserAgent = null,
    string? TraceId = null) : ICommand<AuthenticationResult>;
