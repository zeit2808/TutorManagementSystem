using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

[ApiController, ApiVersion(1.0), Tags("90 · Platform centers")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform")]
public sealed class PlatformCenterLifecycleController(CenterLifecycleWorkflow lifecycle, IUserDirectory users,
    IValidator<CreateCenterRequest> detailsValidator) : CenterControllerBase
{
    [HttpGet("centers/{centerId:guid}")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid centerId, CancellationToken cancellationToken)
    {
        var result = await lifecycle.GetAsync(centerId, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : MapFailure(result.Error);
    }

    [HttpGet("center-counts")]
    [ProducesResponseType<ApiResponse<CenterStatusCountsDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Counts(CancellationToken cancellationToken) =>
        Ok(HttpContext.ToApiResponse(await lifecycle.CountsAsync(cancellationToken)));

    /// <summary>D8: corrects a rejected application and sends it back to PendingApproval.</summary>
    [HttpPost("centers/{centerId:guid}/resubmit"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Resubmit(Guid centerId, [FromBody] ResubmitCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await detailsValidator.ValidateAsync(new CreateCenterRequest(request.LegalName,
            request.DisplayName, request.TaxCode, request.BusinessRegistrationNumber, request.Address,
            request.ContactEmail, request.ContactPhone), cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation);
        if (request.Note?.Length > 1000) return BadRequest(ApiResponse<object>.Failure(
            [new ApiError("validation_error", "note must be at most 1000 characters.", "note")],
            HttpContext.TraceIdentifier, "Request validation failed."));
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();
        var result = await lifecycle.ResubmitAsync(centerId, actorId, new CenterChangeProposal(request.LegalName,
            request.DisplayName, request.TaxCode, request.BusinessRegistrationNumber, request.Address,
            request.ContactEmail, request.ContactPhone), request.Note, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Center resubmitted for approval."))
            : MapFailure(result.Error);
    }

    [HttpGet("centers/{centerId:guid}/history")]
    [ProducesResponseType<ApiResponse<List<CenterStatusChangeDto>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> History(Guid centerId, CancellationToken cancellationToken) =>
        HistoryResponse(lifecycle, users, centerId, cancellationToken);

    /// <summary>Change requests across all centers, oldest first, for the review queue.</summary>
    [HttpGet("change-requests")]
    [ProducesResponseType<ApiResponse<OrganizationPage<CenterChangeRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Queue([FromQuery] string? status = "Pending", [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100)
            return BadRequest(ApiResponse<object>.Failure([new ApiError("invalid_pagination",
                "page must be 1–1000000 and pageSize must be 1–100.")], HttpContext.TraceIdentifier, "Invalid query."));
        CenterChangeStatus? parsed = null;
        if (!string.IsNullOrEmpty(status) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<CenterChangeStatus>(status, true, out var value) || !Enum.IsDefined(value))
                return BadRequest(ApiResponse<object>.Failure([new ApiError("validation_error",
                    "status must be Pending, Approved, Rejected, Withdrawn or All.", "status")],
                    HttpContext.TraceIdentifier, "Invalid query."));
            parsed = value;
        }
        var result = await lifecycle.ChangeQueueAsync(parsed, page, pageSize, cancellationToken);
        await users.FillAsync(result.Items, cancellationToken);
        return Ok(HttpContext.ToApiResponse(result));
    }

    internal static async Task<IActionResult> HistoryResponse(ControllerBase controller, CenterLifecycleWorkflow lifecycle,
        IUserDirectory users, Guid centerId, CancellationToken cancellationToken)
    {
        var result = await lifecycle.HistoryAsync(centerId, cancellationToken);
        if (result.IsFailure) return ((CenterControllerBase)controller).Map(result.Error);
        var names = await users.GetDisplayNamesAsync(result.Value.Where(x => x.ActorUserId.HasValue)
            .Select(x => x.ActorUserId!.Value), cancellationToken);
        foreach (var row in result.Value)
            row.ActorName = row.ActorUserId is { } id ? names.GetValueOrDefault(id) : null;
        return controller.Ok(controller.HttpContext.ToApiResponse(result.Value.ToList()));
    }

    private Task<IActionResult> HistoryResponse(CenterLifecycleWorkflow workflow, IUserDirectory directory,
        Guid centerId, CancellationToken cancellationToken) =>
        HistoryResponse(this, workflow, directory, centerId, cancellationToken);
}

[ApiController, ApiVersion(1.0), Tags("03 · Center profile")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
public sealed class CenterLifecycleController(CenterLifecycleWorkflow lifecycle, IUserDirectory users)
    : CenterControllerBase
{
    /// <summary>D4: the owner decides whether students can find the center in the public directory.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPut("directory"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDirectory(Guid centerId, [FromBody] SetDirectoryListingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await lifecycle.SetDirectoryListingAsync(centerId, request.Listed, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("status-history")]
    [ProducesResponseType<ApiResponse<List<CenterStatusChangeDto>>>(StatusCodes.Status200OK)]
    public Task<IActionResult> History(Guid centerId, CancellationToken cancellationToken) =>
        PlatformCenterLifecycleController.HistoryResponse(this, lifecycle, users, centerId, cancellationToken);

    /// <summary>D10: the owner takes back a change request before the platform reviews it.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPost("change-requests/{requestId:guid}/withdraw")]
    [ProducesResponseType<ApiResponse<CenterChangeRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid centerId, Guid requestId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var ownerId)) return AuthenticationFailure();
        var result = await lifecycle.WithdrawChangeAsync(centerId, requestId, ownerId, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Change request withdrawn."))
            : MapFailure(result.Error);
    }
}
