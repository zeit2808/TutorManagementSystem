using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;

public sealed record ResendVerificationEmailCommand(string Email) : ICommand;
