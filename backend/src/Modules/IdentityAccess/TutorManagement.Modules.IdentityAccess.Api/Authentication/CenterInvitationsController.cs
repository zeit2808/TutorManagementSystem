using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.InviteCenterOwner;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("92 · Platform invitations")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform/centers/{centerId:guid}/invitations")]
public sealed class CenterInvitationsController(
    IValidator<InviteCenterOwnerRequest> validator,
    ICommandHandler<InviteCenterOwnerCommand, CenterInvitationResult> handler)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApiResponse<CenterInvitationResult>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Invite(
        Guid centerId,
        [FromBody] InviteCenterOwnerRequest request,
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

        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var invitedByUserId))
        {
            return Unauthorized(Failure<object>(
                [new Error("unauthorized", "A valid access token is required.")],
                "Authentication required."));
        }

        var result = await handler.HandleAsync(
            new InviteCenterOwnerCommand(centerId, request.Email, invitedByUserId),
            cancellationToken);
        if (result.IsSuccess)
        {
            return StatusCode(
                StatusCodes.Status201Created,
                HttpContext.ToApiResponse(result.Value, "Center owner invitation queued for delivery."));
        }

        if (result.Error == IdentityErrors.InvalidInviterSession)
        {
            return Unauthorized(Failure<object>([result.Error], "Authentication required."));
        }

        if (result.Error == IdentityErrors.CenterNotApproved)
        {
            return UnprocessableEntity(Failure<object>([result.Error], "Invitation was not created."));
        }

        return Conflict(Failure<object>([result.Error], "Invitation was not created."));
    }

    private ApiResponse<T> Failure<T>(IEnumerable<Error> errors, string message) =>
        ApiResponse<T>.Failure(
            errors.Select(error => new ApiError(error.Code, error.Message, error.Field)),
            HttpContext.TraceIdentifier,
            message);
}
