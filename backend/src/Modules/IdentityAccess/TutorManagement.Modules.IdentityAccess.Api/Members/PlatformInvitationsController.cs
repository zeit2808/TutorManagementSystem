using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Members;

/// <summary>Platform view of a center's open invitations; admins manage only the owner invitation.</summary>
[ApiController, ApiVersion(1.0), Tags("92 · Platform invitations")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform/centers/{centerId:guid}/invitations")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class PlatformInvitationsController(PlatformInvitationWorkflow workflow) : ControllerBase
{
    /// <summary>Invitations not yet accepted or revoked, including expired ones.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<List<MemberInvitationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, CancellationToken token) =>
        Ok(HttpContext.ToApiResponse((await workflow.ListAsync(centerId, token)).ToList()));

    [HttpPost("{invitationId:guid}/revoke")]
    [ProducesResponseType<ApiResponse<MemberInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(Guid centerId, Guid invitationId, CancellationToken token)
    {
        var result = await workflow.RevokeAsync(centerId, invitationId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Invitation revoked."))
            : CenterMembersController.Map(this, result.Error);
    }

    /// <summary>Sends the owner invitation again with a new 48-hour link.</summary>
    [HttpPost("{invitationId:guid}/resend")]
    [ProducesResponseType<ApiResponse<MemberInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(Guid centerId, Guid invitationId, CancellationToken token)
    {
        var result = await workflow.ResendAsync(centerId, invitationId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Invitation sent again."))
            : CenterMembersController.Map(this, result.Error);
    }
}
