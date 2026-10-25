using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Api.Authorization;
using TutorManagement.Modules.IdentityAccess.Application.Centers;
using TutorManagement.Modules.IdentityAccess.Application.Centers.GetCenterContext;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("02 · My centers")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/context")]
public sealed class CenterContextController(
    IQueryHandler<GetCenterContextQuery, CenterContextDto> handler) : ControllerBase
{
    [Authorize(Policy = CenterAccessPolicies.Member)]
    [HttpGet]
    [ProducesResponseType<ApiResponse<CenterContextDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid centerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("unauthorized", "A valid access token is required.")],
                HttpContext.TraceIdentifier,
                "Authentication required."));
        }

        var result = await handler.HandleAsync(
            new GetCenterContextQuery(userId, centerId),
            cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse<object>.Failure(
                    [new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
                    HttpContext.TraceIdentifier,
                    "Access denied."));
        }

        return Ok(HttpContext.ToApiResponse(result.Value));
    }
}
