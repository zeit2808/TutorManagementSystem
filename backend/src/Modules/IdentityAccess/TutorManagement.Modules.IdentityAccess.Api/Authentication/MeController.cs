using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("01 · Authentication")]
[Authorize]
[Route("api/v{version:apiVersion}/me")]
public sealed class MeController(IQueryHandler<GetCurrentUserQuery, CurrentUserDto> handler) : ControllerBase
{
    /// <summary>Returns the signed-in account: username, display name, email, phone, roles and linked sign-in methods.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return MissingIdentity();
        return await CurrentAsync(userId, null, cancellationToken);
    }

    /// <summary>Updates the display name and phone number of the signed-in account and returns the account.</summary>
    [HttpPut("profile")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateMyProfileRequest request,
        [FromServices] IValidator<UpdateMyProfileRequest> validator,
        [FromServices] ICommandHandler<UpdateMyProfileCommand> updateHandler,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return MissingIdentity();
        var errors = await ValidateAsync(validator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure(errors, "Request validation failed."));

        var result = await updateHandler.HandleAsync(new UpdateMyProfileCommand(userId, request.DisplayName,
            request.PhoneNumber, ClientIp, UserAgent, HttpContext.TraceIdentifier), cancellationToken);
        if (result.IsFailure) return FailureResult(result.Error, "Profile update failed.");
        return await CurrentAsync(userId, "Profile updated.", cancellationToken);
    }

    /// <summary>Step 1 of changing the sign-in email: checks the password and sends an eight-digit code to the new address.</summary>
    [HttpPost("email/change")]
    [EnableRateLimiting("password-authenticated")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<EmailChangeStartedResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestEmailChange([FromBody] RequestEmailChangeRequest request,
        [FromServices] IValidator<RequestEmailChangeRequest> validator,
        [FromServices] ICommandHandler<RequestEmailChangeCommand, EmailChangeStarted> requestHandler,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return MissingIdentity();
        var errors = await ValidateAsync(validator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure(errors, "Request validation failed."));

        var result = await requestHandler.HandleAsync(new RequestEmailChangeCommand(userId, request.NewEmail,
            request.CurrentPassword, ClientIp, UserAgent, HttpContext.TraceIdentifier), cancellationToken);
        if (result.IsFailure) return FailureResult(result.Error, "Email change could not start.");
        Response.Headers.CacheControl = "no-store";
        return Ok(HttpContext.ToApiResponse(new EmailChangeStartedResponse(result.Value.NewEmail,
            result.Value.ExpiresAtUtc, result.Value.ResendAvailableAtUtc), "A confirmation code was sent to the new email."));
    }

    /// <summary>Step 2: confirms the code sent to the new address; the sign-in email changes and returns the account.</summary>
    [HttpPost("email/confirm")]
    [EnableRateLimiting("password-authenticated")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequest request,
        [FromServices] IValidator<ConfirmEmailChangeRequest> validator,
        [FromServices] ICommandHandler<ConfirmEmailChangeCommand> confirmHandler,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return MissingIdentity();
        var errors = await ValidateAsync(validator, request, cancellationToken);
        if (errors.Count > 0) return BadRequest(Failure(errors, "Request validation failed."));

        var result = await confirmHandler.HandleAsync(new ConfirmEmailChangeCommand(userId, request.NewEmail,
            request.Otp, ClientIp, UserAgent, HttpContext.TraceIdentifier), cancellationToken);
        if (result.IsFailure) return FailureResult(result.Error, "Email change failed.");
        return await CurrentAsync(userId, "Sign-in email changed.", cancellationToken);
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private string? UserAgent => Request.Headers.UserAgent.ToString();

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirst("sub")?.Value, out userId);

    private async Task<IActionResult> CurrentAsync(Guid userId, string? message, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetCurrentUserQuery(userId), cancellationToken);
        if (result.IsFailure)
            return Unauthorized(Failure([result.Error], "Authentication required."));
        return Ok(message is null
            ? HttpContext.ToApiResponse(result.Value)
            : HttpContext.ToApiResponse(result.Value, message));
    }

    private UnauthorizedObjectResult MissingIdentity() =>
        Unauthorized(Failure([new Error("unauthorized", "A valid access token is required.")], "Authentication required."));

    private IActionResult FailureResult(Error error, string message)
    {
        var response = Failure([error], message);
        if (error == CurrentUserErrors.NotFound) return Unauthorized(response);
        if (error == IdentityErrors.EmailAlreadyExists || error == IdentityErrors.PhoneNumberAlreadyExists)
            return Conflict(response);
        if (error == IdentityErrors.EmailChangeTooFrequent)
            return StatusCode(StatusCodes.Status429TooManyRequests, response);
        return BadRequest(response);
    }

    private static async Task<IReadOnlyCollection<Error>> ValidateAsync<T>(
        IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        return result.Errors.Select(error => new Error("validation_error", error.ErrorMessage, error.PropertyName)).ToArray();
    }

    private ApiResponse<object> Failure(IReadOnlyCollection<Error> errors, string message) =>
        ApiResponse<object>.Failure(errors.Select(error => new ApiError(error.Code, error.Message, error.Field)).ToArray(),
            HttpContext.TraceIdentifier, message);
}
