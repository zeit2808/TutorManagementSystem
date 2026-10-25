using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.IdentityAccess.Api.Authentication;

[ApiController]
[ApiVersion(1.0)]
[Tags("01 · Authentication")]
[Authorize]
[Route("api/v{version:apiVersion}/me")]
public sealed class MeController(IQueryHandler<GetCurrentUserQuery, CurrentUserDto> handler) : ControllerBase
{
    /// <summary>Returns the signed-in account: username, email, roles and which sign-in methods are linked.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<CurrentUserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError("unauthorized", "A valid access token is required.")],
                HttpContext.TraceIdentifier, "Authentication required."));

        var result = await handler.HandleAsync(new GetCurrentUserQuery(userId), cancellationToken);
        if (result.IsFailure)
            return Unauthorized(ApiResponse<object>.Failure(
                [new ApiError(result.Error.Code, result.Error.Message, result.Error.Field)],
                HttpContext.TraceIdentifier, "Authentication required."));

        return Ok(HttpContext.ToApiResponse(result.Value));
    }
}
