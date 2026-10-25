using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Members;

[ApiController, ApiVersion(1.0), Tags("02 · Center members")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class CenterMembersController(CenterMemberWorkflow workflow) : ControllerBase
{
    /// <summary>Everyone with a role in the center, active first. Owners and admins can see it.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("members")]
    [ProducesResponseType<ApiResponse<List<CenterMemberDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, CancellationToken token) =>
        Ok(HttpContext.ToApiResponse((await workflow.ListAsync(centerId, token)).ToList()));

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("member-invitations")]
    [ProducesResponseType<ApiResponse<List<MemberInvitationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Invitations(Guid centerId, CancellationToken token) =>
        Ok(HttpContext.ToApiResponse((await workflow.ListInvitationsAsync(centerId, token)).ToList()));

    /// <summary>Invites an admin, teacher or staff member by email. Existing accounts are reused.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("member-invitations"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<MemberInvitationDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Invite(Guid centerId, [FromBody] InviteMemberRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.InviteAsync(centerId, actorId, request.Email, request.Role, token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Invitation queued for delivery."))
            : Fail(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("member-invitations/{invitationId:guid}/revoke")]
    [ProducesResponseType<ApiResponse<MemberInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Revoke(Guid centerId, Guid invitationId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.RevokeInvitationAsync(centerId, actorId, invitationId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    /// <summary>Sends the invitation again with a new link; the previous link stops working.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("member-invitations/{invitationId:guid}/resend")]
    [ProducesResponseType<ApiResponse<MemberInvitationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(Guid centerId, Guid invitationId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.ResendInvitationAsync(centerId, actorId, invitationId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Invitation sent again.")) : Fail(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPut("members/{membershipId:guid}/role"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<CenterMemberDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeRole(Guid centerId, Guid membershipId,
        [FromBody] ChangeMemberRoleRequest request, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.ChangeRoleAsync(centerId, actorId, membershipId, request.Role, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    /// <summary>Removes access immediately; the membership row and its history are kept.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("members/{membershipId:guid}/deactivate")]
    [ProducesResponseType<ApiResponse<CenterMemberDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid centerId, Guid membershipId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.DeactivateAsync(centerId, actorId, membershipId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("members/{membershipId:guid}/reactivate")]
    [ProducesResponseType<ApiResponse<CenterMemberDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid membershipId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.ReactivateAsync(centerId, actorId, membershipId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    private bool TryGetActor(out Guid actorId) => Guid.TryParse(User.FindFirst("sub")?.Value, out actorId);

    internal static IActionResult Map(ControllerBase controller, Error error)
    {
        var response = ApiResponse<object>.Failure([new ApiError(error.Code, error.Message, error.Field)],
            controller.HttpContext.TraceIdentifier, error.Message);
        if (error == MemberErrors.OwnerOnly) return controller.StatusCode(StatusCodes.Status403Forbidden, response);
        if (error == MemberErrors.NotFound || error == MemberErrors.InvitationNotFound) return controller.NotFound(response);
        if (error == MemberErrors.InvalidRole || error == MemberErrors.InvalidEmail) return controller.BadRequest(response);
        if (error == IdentityErrors.CenterNotApproved || error == MemberErrors.OwnerProtected)
            return controller.UnprocessableEntity(response);
        return controller.Conflict(response);
    }

    private IActionResult Fail(Error error) => Map(this, error);

    private UnauthorizedObjectResult InvalidIdentity() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("unauthorized", "A valid access token is required.")],
        HttpContext.TraceIdentifier, "Authentication required."));
}
