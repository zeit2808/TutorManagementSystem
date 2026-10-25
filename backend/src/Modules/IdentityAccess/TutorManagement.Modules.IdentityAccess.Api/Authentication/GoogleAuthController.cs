using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController, ApiVersion(1.0), Tags("01 · Authentication")]
[Route("api/v{version:apiVersion}/auth/google")]
public sealed class GoogleAuthController(
    ICommandHandler<GoogleLoginCommand, AuthenticationResult> handler,
    ICommandHandler<LinkGoogleCommand> linkHandler,
    IRefreshTokenCookieManager cookies) : ControllerBase
{
    [AllowAnonymous, EnableRateLimiting("login"), HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Login([FromBody] GoogleLoginRequest request, CancellationToken token)
    {
        var result = await handler.HandleAsync(new GoogleLoginCommand(request.IdToken,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier), token);
        if (result.IsFailure)
        {
            var response = ApiResponse<object>.Failure(
                [new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
                HttpContext.TraceIdentifier, "Google sign-in failed.");
            return result.Error == IdentityErrors.GoogleAccountLinkRequired ? Conflict(response) : Unauthorized(response);
        }

        cookies.Append(HttpContext, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(HttpContext.ToApiResponse(new TokenResponse
        {
            AccessToken = result.Value.AccessToken,
            ExpiresIn = result.Value.ExpiresInSeconds
        }, "Google sign-in successful."));
    }

    [Authorize, EnableRateLimiting("password-authenticated"), HttpPost("link")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Link([FromBody] LinkGoogleRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("invalid_identity", "A valid user identity is required.")],
                HttpContext.TraceIdentifier, "Unauthorized."));
        var result = await linkHandler.HandleAsync(new LinkGoogleCommand(userId, request.IdToken,
            request.CurrentPassword,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(),
            HttpContext.TraceIdentifier), token);
        if (result.IsFailure)
        {
            var response = ApiResponse<object>.Failure(
                [new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
                HttpContext.TraceIdentifier, result.Error.Message);
            return result.Error == IdentityErrors.GoogleAlreadyLinked ? Conflict(response) : Unauthorized(response);
        }
        return Ok(HttpContext.ToApiResponse(new { linked = true }, "Google account linked. You can now sign in to this account with Google."));
    }
}
