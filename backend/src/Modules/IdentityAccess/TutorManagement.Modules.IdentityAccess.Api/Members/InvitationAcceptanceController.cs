using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Members;

[ApiController, ApiVersion(1.0), Tags("01 · Authentication")]
[Route("api/v{version:apiVersion}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class InvitationAcceptanceController(CenterMemberWorkflow workflow) : ControllerBase
{
    /// <summary>
    /// Shows which center, role and email an invitation is for, and whether that email already has
    /// an account (sign in and accept) or not (create the account with auth/invitations/accept).
    /// </summary>
    [AllowAnonymous, HttpPost("auth/invitations/preview"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<InvitationPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview([FromBody] InvitationTokenRequest request, CancellationToken token)
    {
        var result = await workflow.PreviewAsync(request.Token ?? string.Empty, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    /// <summary>Accepts an invitation with the signed-in account; its email must match the invitation.</summary>
    [Authorize, HttpPost("me/invitations/accept"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<AcceptedMembershipDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Accept([FromBody] InvitationTokenRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("unauthorized", "A valid access token is required.")],
                HttpContext.TraceIdentifier, "Authentication required."));
        var result = await workflow.AcceptAsSignedInAsync(userId, request.Token ?? string.Empty, token);
        return result.IsSuccess
            ? Ok(HttpContext.ToApiResponse(result.Value, "Invitation accepted."))
            : Fail(result.Error);
    }

    private IActionResult Fail(Error error)
    {
        var response = ApiResponse<object>.Failure([new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier, error.Message);
        return error == IdentityErrors.InvalidCenterInvitation || error == IdentityErrors.CenterNotApproved
            ? UnprocessableEntity(response)
            : Conflict(response);
    }
}
