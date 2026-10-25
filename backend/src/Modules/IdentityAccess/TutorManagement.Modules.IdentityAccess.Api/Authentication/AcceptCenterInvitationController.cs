using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.AcceptCenterInvitation;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("01 · Authentication")]
[Route("api/v{version:apiVersion}/auth/invitations")]
public sealed class AcceptCenterInvitationController(
    IValidator<AcceptCenterInvitationRequest> validator,
    ICommandHandler<AcceptCenterInvitationCommand, AcceptedInvitationResult> handler)
    : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("accept")]
    [ProducesResponseType<ApiResponse<AcceptedInvitationResult>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Accept(
        [FromBody] AcceptCenterInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(Failure<object>(
                validation.Errors.Select(failure => new Error(
                    "validation_error", failure.ErrorMessage, failure.PropertyName)),
                "Request validation failed."));
        }

        var result = await handler.HandleAsync(
            new AcceptCenterInvitationCommand(
                request.Token, request.Username, request.Password, request.PhoneNumber),
            cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(HttpContext.ToApiResponse(
                result.Value,
                "Invitation accepted. You can now sign in."));
        }

        if (result.Error == IdentityErrors.InvalidCenterInvitation ||
            result.Error == IdentityErrors.CenterNotApproved)
        {
            return UnprocessableEntity(Failure<object>([result.Error], "Invitation acceptance failed."));
        }

        return Conflict(Failure<object>([result.Error], "Invitation acceptance failed."));
    }

    private ApiResponse<T> Failure<T>(IEnumerable<Error> errors, string message) =>
        ApiResponse<T>.Failure(
            errors.Select(error => new ApiError(error.Code, error.Message, error.Field)),
            HttpContext.TraceIdentifier,
            message);
}
