using Microsoft.AspNetCore.Http;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public sealed class RefreshTokenCookieOptions
{
    public const string SectionName = "Authentication:RefreshCookie";

    public string Name { get; set; } = "tm.refresh";

    public string Path { get; set; } = "/api/v1/auth";

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Lax;

    public bool Secure { get; set; } = true;
}
