using TutorManagement.Application.Abstractions.Messaging;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public sealed record ResetPasswordCommand(string Email, string Otp, string NewPassword) : ICommand;
