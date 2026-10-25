using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

public sealed class PasswordResetOtpService(IOptions<JwtOptions> options) : IPasswordResetOtpService
{
    private readonly byte[] _key = HMACSHA256.HashData(
        Convert.FromBase64String(options.Value.SigningKey),
        Encoding.UTF8.GetBytes("TutorManagement/password-reset-otp/v1"));

    public IssuedPasswordResetOtp Create(Guid userId, Guid tokenId)
    {
        var value = RandomNumberGenerator.GetInt32(100_000_000).ToString("D8", CultureInfo.InvariantCulture);
        return new IssuedPasswordResetOtp(value, ComputeHash(userId, tokenId, value));
    }

    public bool Verify(Guid userId, Guid tokenId, string submittedOtp, string expectedHash)
    {
        if (submittedOtp.Length != 8 || !submittedOtp.All(char.IsAsciiDigit)) return false;
        var actual = Convert.FromHexString(ComputeHash(userId, tokenId, submittedOtp));
        var expected = Convert.FromHexString(expectedHash);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private string ComputeHash(Guid userId, Guid tokenId, string otp) =>
        Convert.ToHexString(HMACSHA256.HashData(
            _key,
            Encoding.UTF8.GetBytes($"{userId:N}:{tokenId:N}:{otp}")));
}
