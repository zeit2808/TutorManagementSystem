using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

[ApiController, ApiVersion(1.0), Tags("09 · Teachers")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class TeacherAccountController(TeacherAccountWorkflow workflow) : TeachingControllerBase
{
    /// <summary>
    /// Links a teacher profile to a member's login, or unlinks it with userId = null. The owner can link
    /// their own account to teach in their center without a second login.
    /// </summary>
    [Authorize(Policy = CenterAccessPolicyNames.Owner), HttpPut("teachers/{teacherId:guid}/account")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Link(Guid centerId, Guid teacherId, [FromBody] LinkTeacherAccountRequest request,
        CancellationToken token)
    {
        if (request.ExpectedVersion is not >= 0)
            return ValidationFailure([new FluentValidation.Results.ValidationFailure(
                "ExpectedVersion", "expectedVersion is required and must be zero or greater.")]);
        var result = await workflow.LinkAsync(centerId, teacherId, request.UserId, request.ExpectedVersion.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>The caller's teacher profile in this center (if linked) and its sessions in the range.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("my/teaching")]
    [ProducesResponseType<ApiResponse<MyTeachingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyTeaching(Guid centerId, [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc, CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized(ApiResponse<object>.Failure(
            [new ApiError("invalid_identity", "A valid user identity is required.")],
            HttpContext.TraceIdentifier, "Unauthorized."));
        var result = await workflow.MyTeachingAsync(centerId, userId, fromUtc, toUtc, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }
}
