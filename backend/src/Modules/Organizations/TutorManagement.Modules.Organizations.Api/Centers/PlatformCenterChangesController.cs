using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.ListCenterChanges;
using TutorManagement.Modules.Organizations.Application.Centers.ReviewCenterChange;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("91 · Platform review")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform/centers/{centerId:guid}/change-requests")]
public sealed class PlatformCenterChangesController(
    IValidator<ReviewCenterChangeRequest> validator,
    IQueryHandler<ListCenterChangesQuery, List<CenterChangeRequestDto>> listHandler,
    ICommandHandler<ReviewCenterChangeCommand, CenterChangeRequestDto> reviewHandler,
    IUserDirectory users)
    : CenterControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<List<CenterChangeRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, CancellationToken cancellationToken)
    {
        var result = await listHandler.HandleAsync(new ListCenterChangesQuery(centerId), cancellationToken);
        return result.IsSuccess
            ? Ok(HttpContext.ToApiResponse(await users.FillAsync(result.Value, cancellationToken)))
            : MapFailure(result.Error);
    }

    [HttpPost("{requestId:guid}/approve")]
    [ProducesResponseType<ApiResponse<CenterChangeRequestDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Approve(
        Guid centerId, Guid requestId, [FromBody] ReviewCenterChangeRequest request,
        CancellationToken cancellationToken) =>
        Review(centerId, requestId, request, approve: true, cancellationToken);

    [HttpPost("{requestId:guid}/reject")]
    [ProducesResponseType<ApiResponse<CenterChangeRequestDto>>(StatusCodes.Status200OK)]
    public Task<IActionResult> Reject(
        Guid centerId, Guid requestId, [FromBody] ReviewCenterChangeRequest request,
        CancellationToken cancellationToken) =>
        Review(centerId, requestId, request, approve: false, cancellationToken);

    private async Task<IActionResult> Review(
        Guid centerId, Guid requestId, ReviewCenterChangeRequest request,
        bool approve, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation);
        if (!TryGetUserId(out var reviewerId)) return AuthenticationFailure();
        var result = await reviewHandler.HandleAsync(
            new ReviewCenterChangeCommand(
                centerId, requestId, reviewerId, approve, request.ReviewNote),
            cancellationToken);
        return result.IsSuccess
            ? Ok(HttpContext.ToApiResponse(result.Value,
                approve ? "Center change approved." : "Center change rejected."))
            : MapFailure(result.Error);
    }
}
