namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public sealed record IssuedEmailChangeOtp(string Value, string Hash);

/// <summary>
/// Eight-digit codes for moving the sign-in email. The hash binds the code to the new address, so a code
/// sent to one address cannot confirm a different one.
/// </summary>
public interface IEmailChangeOtpService
{
    IssuedEmailChangeOtp Create(Guid userId, Guid tokenId, string normalizedNewEmail);
    bool Verify(Guid userId, Guid tokenId, string normalizedNewEmail, string submittedOtp, string expectedHash);
}
