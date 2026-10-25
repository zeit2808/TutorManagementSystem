using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

public sealed class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";
    public string ClientId { get; set; } = string.Empty;
}

public sealed class GoogleIdentityVerifier(IOptions<GoogleAuthOptions> options) : IGoogleIdentityVerifier
{
    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken token)
    {
        var clientId = options.Value.ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("GoogleAuth:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > 16_384) return null;
        token.ThrowIfCancellationRequested();
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(payload.Subject) || string.IsNullOrWhiteSpace(payload.Email)) return null;
            return new GoogleIdentity(payload.Subject, payload.Email, payload.EmailVerified, payload.HostedDomain);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
