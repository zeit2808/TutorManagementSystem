using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("01 · Authentication")]
[Route("api/v{version:apiVersion}/auth/password")]
public sealed class PasswordController(
    IValidator<ChangePasswordRequest> changeValidator,
    IValidator<ForgotPasswordRequest> forgotValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    ICommandHandler<ChangePasswordCommand, AuthenticationResult> changeHandler,
    ICommandHandler<ForgotPasswordCommand> forgotHandler,
    ICommandHandler<ResetPasswordCommand> resetHandler,
    IRefreshTokenCookieManager cookieManager) : ControllerBase
{
    [Authorize]
    [EnableRateLimiting("password-authenticated")]
    [HttpPost("change")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TokenResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiResponse<TokenResponse>>> Change(
        [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(changeValidator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure<TokenResponse>(errors, "Request validation failed."));

        var subject = User.FindFirst("sub")?.Value;
        var sid = User.FindFirst("sid")?.Value;
        var refresh = cookieManager.Read(HttpContext);
        if (!Guid.TryParse(subject, out var userId) || !Guid.TryParse(sid, out var sessionId) ||
            string.IsNullOrWhiteSpace(refresh))
            return Unauthorized(Failure<TokenResponse>([IdentityErrors.InvalidRefreshToken], "Authentication required."));

        var result = await changeHandler.HandleAsync(
            new ChangePasswordCommand(userId, sessionId, refresh, request.CurrentPassword, request.NewPassword),
            cancellationToken);
        if (result.IsFailure)
        {
            var response = Failure<TokenResponse>([result.Error], "Password change failed.");
            if (result.Error == IdentityErrors.InvalidRefreshToken)
            {
                cookieManager.Delete(HttpContext);
                return Unauthorized(response);
            }
            return BadRequest(response);
        }

        cookieManager.Append(HttpContext, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(HttpContext.ToApiResponse(new TokenResponse
        {
            AccessToken = result.Value.AccessToken,
            ExpiresIn = result.Value.ExpiresInSeconds
        }, "Password changed. Continue with this session and replace the access token."));
    }

    [AllowAnonymous]
    [EnableRateLimiting("password-recovery")]
    [HttpPost("forgot")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Forgot([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(forgotValidator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure<object>(errors, "Request validation failed."));
        await forgotHandler.HandleAsync(new ForgotPasswordCommand(request.Email), cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Accepted(HttpContext.ToApiResponse<object?>(null,
            "If the address belongs to an eligible account, a reset code will be sent."));
    }

    [AllowAnonymous]
    [EnableRateLimiting("password-reset")]
    [HttpPost("reset")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Reset([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = await ValidateAsync(resetValidator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure<object>(errors, "Request validation failed."));
        var result = await resetHandler.HandleAsync(
            new ResetPasswordCommand(request.Email, request.Otp, request.NewPassword), cancellationToken);
        if (result.IsFailure) return BadRequest(Failure<object>([result.Error], "Password reset failed."));
        cookieManager.Delete(HttpContext);
        return NoContent();
    }

    private static async Task<IReadOnlyCollection<Error>> ValidateAsync<T>(
        IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.Errors.Select(error => new Error("validation_error", error.ErrorMessage, error.PropertyName)).ToArray();
    }

    private ApiResponse<T> Failure<T>(IReadOnlyCollection<Error> errors, string message) =>
        ApiResponse<T>.Failure(errors.Select(error => new ApiError(error.Code, error.Message, error.Field)).ToArray(),
            HttpContext.TraceIdentifier, message);
}
