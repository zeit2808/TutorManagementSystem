namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public sealed record IssuedPasswordResetOtp(string Value, string Hash);

public interface IPasswordResetOtpService
{
    IssuedPasswordResetOtp Create(Guid userId, Guid tokenId);
    bool Verify(Guid userId, Guid tokenId, string submittedOtp, string expectedHash);
}
