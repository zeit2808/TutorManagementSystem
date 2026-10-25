namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed class TokenResponse
{
    public string TokenType { get; set; } = "Bearer";

    public string AccessToken { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }
}
