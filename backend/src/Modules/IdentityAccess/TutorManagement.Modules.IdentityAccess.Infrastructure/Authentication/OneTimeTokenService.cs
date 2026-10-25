using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

public sealed class OneTimeTokenService : IOneTimeTokenService
{
    public IssuedOneTimeToken Create()
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new IssuedOneTimeToken(value, Hash(value));
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }
}
