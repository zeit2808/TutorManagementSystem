using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

public interface IRefreshTokenCookieManager
{
    string? Read(HttpContext httpContext);

    void Append(HttpContext httpContext, string refreshToken, DateTimeOffset expiresAtUtc);

    void Delete(HttpContext httpContext);
}

public sealed class RefreshTokenCookieManager(
    IOptions<RefreshTokenCookieOptions> options) : IRefreshTokenCookieManager
{
    private readonly RefreshTokenCookieOptions _options = options.Value;

    public string? Read(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Request.Cookies.TryGetValue(_options.Name, out var refreshToken)
            ? refreshToken
            : null;
    }

    public void Append(
        HttpContext httpContext,
        string refreshToken,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        httpContext.Response.Cookies.Append(
            _options.Name,
            refreshToken,
            CreateCookieOptions(expiresAtUtc));

        SetNoStoreHeaders(httpContext.Response);
    }

    public void Delete(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        httpContext.Response.Cookies.Delete(
            _options.Name,
            CreateCookieOptions(DateTimeOffset.UnixEpoch));

        SetNoStoreHeaders(httpContext.Response);
    }

    private CookieOptions CreateCookieOptions(DateTimeOffset expiresAtUtc) =>
        new()
        {
            HttpOnly = true,
            Secure = _options.Secure,
            SameSite = _options.SameSite,
            Path = _options.Path,
            Expires = expiresAtUtc,
            IsEssential = true
        };

    private static void SetNoStoreHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.Pragma = "no-cache";
    }
}
