namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed class VerifyEmailRequest
{
    public string Token { get; init; } = string.Empty;
}
