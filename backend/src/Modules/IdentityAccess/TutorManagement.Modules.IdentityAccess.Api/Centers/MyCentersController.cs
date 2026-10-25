using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Centers;
using TutorManagement.Modules.IdentityAccess.Application.Centers.GetMyCenters;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("02 · My centers")]
[Authorize]
[Route("api/v{version:apiVersion}/me/centers")]
public sealed class MyCentersController(
    IQueryHandler<GetMyCentersQuery, List<CenterContextDto>> handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<List<CenterContextDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("unauthorized", "A valid access token is required.")],
                HttpContext.TraceIdentifier,
                "Authentication required."));
        }

        var result = await handler.HandleAsync(
            new GetMyCentersQuery(userId),
            cancellationToken);
        return Ok(HttpContext.ToApiResponse(result.Value));
    }
}
