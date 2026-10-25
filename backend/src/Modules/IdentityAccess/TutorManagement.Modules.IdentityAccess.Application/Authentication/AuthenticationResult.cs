namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public sealed record AuthenticationResult(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc);
