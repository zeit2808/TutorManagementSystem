using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.GetCenter;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("03 · Center profile")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/profile")]
public sealed class CenterProfileController(
    IQueryHandler<GetCenterQuery, CenterDto> getHandler)
    : CenterControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member)]
    [HttpGet]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, CancellationToken cancellationToken)
    {
        var result = await getHandler.HandleAsync(new GetCenterQuery(centerId), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : MapFailure(result.Error);
    }
}
