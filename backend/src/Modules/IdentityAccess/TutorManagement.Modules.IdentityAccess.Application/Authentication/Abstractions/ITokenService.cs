using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public sealed record IssuedAccessToken(string Value, DateTimeOffset ExpiresAtUtc);

public sealed record IssuedRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAtUtc);

public interface ITokenService
{
    IssuedAccessToken CreateAccessToken(User user, IReadOnlyCollection<string> roles, Guid sessionId);

    IssuedRefreshToken CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
