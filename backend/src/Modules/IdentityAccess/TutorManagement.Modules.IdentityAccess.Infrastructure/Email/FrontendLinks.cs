using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

/// <summary>Reads <c>Frontend:BaseUrl</c> (e.g. https://app.example.com). Without it, emails carry the token only.</summary>
public sealed class FrontendLinks(IServiceProvider services) : IFrontendLinks
{
    private readonly string? baseUrl = Normalize(services.GetService<IConfiguration>()?["Frontend:BaseUrl"]);

    public string? InvitationUrl(string invitationToken) => baseUrl is null
        ? null
        : $"{baseUrl}/accept-invitation?token={Uri.EscapeDataString(invitationToken)}";

    public string? VerifyEmailUrl(string verificationToken) => baseUrl is null
        ? null
        : $"{baseUrl}/verify-email?token={Uri.EscapeDataString(verificationToken)}";

    private static string? Normalize(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
            ? uri.GetLeftPart(UriPartial.Path).TrimEnd('/')
            : null;
}
