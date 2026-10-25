using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.ListCenterChanges;
using TutorManagement.Modules.Organizations.Application.Centers.RequestCenterChange;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("04 · Center change requests")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/change-requests")]
public sealed class CenterChangesController(
    IValidator<RequestCenterChangeRequest> validator,
    ICommandHandler<RequestCenterChangeCommand, CenterChangeRequestDto> createHandler,
    IQueryHandler<ListCenterChangesQuery, List<CenterChangeRequestDto>> listHandler,
    IUserDirectory users)
    : CenterControllerBase
{
    /// <summary>D10: owners and admins see the requests; only the owner submits or withdraws.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpGet]
    [ProducesResponseType<ApiResponse<List<CenterChangeRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, CancellationToken cancellationToken)
    {
        var result = await listHandler.HandleAsync(new ListCenterChangesQuery(centerId), cancellationToken);
        return result.IsSuccess
            ? Ok(HttpContext.ToApiResponse(await users.FillAsync(result.Value, cancellationToken)))
            : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Owner)]
    [HttpPost]
    [ProducesResponseType<ApiResponse<CenterChangeRequestDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(
        Guid centerId, [FromBody] RequestCenterChangeRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation);
        if (!TryGetUserId(out var userId)) return AuthenticationFailure();

        var result = await createHandler.HandleAsync(
            new RequestCenterChangeCommand(
                centerId, userId,
                new CenterChangeProposal(
                    request.LegalName, request.DisplayName, request.TaxCode,
                    request.BusinessRegistrationNumber, request.RegisteredAddress,
                    request.ContactEmail, request.ContactPhone),
                request.Reason, request.EvidenceReference),
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created,
                HttpContext.ToApiResponse(result.Value, "Center change submitted for review."))
            : MapFailure(result.Error);
    }
}
