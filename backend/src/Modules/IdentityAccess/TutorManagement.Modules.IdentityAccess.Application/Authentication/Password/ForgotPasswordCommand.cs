using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed record ForgotPasswordCommand(string Email) : ICommand;
