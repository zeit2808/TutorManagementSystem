using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.VerifyEmail;

public sealed record VerifyEmailCommand(string Token) : ICommand;
