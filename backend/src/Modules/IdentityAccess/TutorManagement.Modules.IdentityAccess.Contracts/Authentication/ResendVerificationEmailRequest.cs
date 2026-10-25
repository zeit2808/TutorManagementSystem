namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed class ResendVerificationEmailRequest
{
    public string Email { get; init; } = string.Empty;
}
