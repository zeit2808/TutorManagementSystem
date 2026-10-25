using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.VerifyEmail;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("01 · Authentication")]
[Route("api/v{version:apiVersion}/auth")]
public sealed class AuthController(
    IValidator<LoginRequest> loginValidator,
    IValidator<ResendVerificationEmailRequest> resendVerificationEmailValidator,
    IValidator<VerifyEmailRequest> verifyEmailValidator,
    ICommandHandler<LoginCommand, AuthenticationResult> loginHandler,
    ICommandHandler<ResendVerificationEmailCommand> resendVerificationEmailHandler,
    ICommandHandler<VerifyEmailCommand> verifyEmailHandler,
    ICommandHandler<RefreshTokenCommand, AuthenticationResult> refreshTokenHandler,
    ICommandHandler<LogoutCommand> logoutHandler,
    ICommandHandler<LogoutAllCommand> logoutAllHandler,
    IRefreshTokenCookieManager refreshTokenCookieManager)
    : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("verify-email")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = await ValidateAsync(
            verifyEmailValidator,
            request,
            cancellationToken);
        if (validationErrors.Count > 0)
        {
            return BadRequest(
                CreateValidationFailureResponse<object>(validationErrors));
        }

        var result = await verifyEmailHandler.HandleAsync(
            new VerifyEmailCommand(request.Token),
            cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(CreateFailureResponse<object>(
                result.Error,
                "Email verification failed."));
        }

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("resend-verification-email")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResendVerificationEmail(
        [FromBody] ResendVerificationEmailRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = await ValidateAsync(
            resendVerificationEmailValidator,
            request,
            cancellationToken);
        if (validationErrors.Count > 0)
        {
            return BadRequest(
                CreateValidationFailureResponse<object>(validationErrors));
        }

        await resendVerificationEmailHandler.HandleAsync(
            new ResendVerificationEmailCommand(request.Email),
            cancellationToken);

        return NoContent();
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    [Consumes("application/json")]
    [Produces("application/json", "application/xml")]
    [ProducesResponseType<ApiResponse<TokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var validationErrors = await ValidateAsync(
            loginValidator,
            request,
            cancellationToken);
        if (validationErrors.Count > 0)
        {
            return BadRequest(
                CreateValidationFailureResponse<TokenResponse>(validationErrors));
        }

        var result = await loginHandler.HandleAsync(
            new LoginCommand(request.Username, request.Password,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                Request.Headers.UserAgent.ToString(), HttpContext.TraceIdentifier),
            cancellationToken);

        if (result.IsFailure)
        {
            return Unauthorized(CreateFailureResponse<TokenResponse>(
                result.Error,
                "Authentication failed."));
        }

        refreshTokenCookieManager.Append(
            HttpContext,
            result.Value.RefreshToken,
            result.Value.RefreshTokenExpiresAtUtc);

        return Ok(HttpContext.ToApiResponse(
            MapTokenResponse(result.Value),
            "Login successful."));
    }

    [AllowAnonymous]
    [HttpPost("refresh-token")]
    [Produces("application/json", "application/xml")]
    [ProducesResponseType<ApiResponse<TokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> RefreshToken(
        CancellationToken cancellationToken)
    {
        var refreshToken = refreshTokenCookieManager.Read(HttpContext);
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(CreateFailureResponse<TokenResponse>(
                IdentityErrors.InvalidRefreshToken,
                "Token refresh failed."));
        }

        var result = await refreshTokenHandler.HandleAsync(
            new RefreshTokenCommand(refreshToken),
            cancellationToken);

        if (result.IsFailure)
        {
            refreshTokenCookieManager.Delete(HttpContext);
            return Unauthorized(CreateFailureResponse<TokenResponse>(
                result.Error,
                "Token refresh failed."));
        }

        refreshTokenCookieManager.Append(
            HttpContext,
            result.Value.RefreshToken,
            result.Value.RefreshTokenExpiresAtUtc);

        return Ok(HttpContext.ToApiResponse(
            MapTokenResponse(result.Value),
            "Token refreshed successfully."));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = refreshTokenCookieManager.Read(HttpContext);
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await logoutHandler.HandleAsync(
                new LogoutCommand(refreshToken),
                cancellationToken);
        }

        refreshTokenCookieManager.Delete(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Unauthorized(CreateFailureResponse<object>(
                new Error("unauthorized", "A valid access token is required."),
                "Authentication required."));
        }

        await logoutAllHandler.HandleAsync(
            new LogoutAllCommand(userId),
            cancellationToken);

        refreshTokenCookieManager.Delete(HttpContext);
        return NoContent();
    }

    private static async Task<IReadOnlyCollection<Error>> ValidateAsync<T>(
        IValidator<T> validator,
        T request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid)
        {
            return Array.Empty<Error>();
        }

        return validationResult.Errors
            .Select(failure => new Error(
                "validation_error",
                failure.ErrorMessage,
                failure.PropertyName))
            .ToArray();
    }

    private ApiResponse<T> CreateValidationFailureResponse<T>(
        IReadOnlyCollection<Error> errors) =>
        ApiResponse<T>.Failure(
            errors
                .Select(error => new ApiError(
                    error.Code,
                    error.Message,
                    error.Field))
                .ToArray(),
            HttpContext.TraceIdentifier,
            "Request validation failed.");

    private ApiResponse<T> CreateFailureResponse<T>(Error error, string message) =>
        ApiResponse<T>.Failure(
            [new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier,
            message);

    private static TokenResponse MapTokenResponse(AuthenticationResult result) =>
        new()
        {
            AccessToken = result.AccessToken,
            ExpiresIn = result.ExpiresInSeconds
        };
}
