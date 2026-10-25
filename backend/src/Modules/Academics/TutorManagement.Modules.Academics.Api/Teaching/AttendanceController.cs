using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

[ApiController, ApiVersion(1.0), Tags("12 · Attendance")]
// Any member may call; non-managers are limited to their own sessions inside the workflow.
[Authorize(Policy = CenterAccessPolicyNames.Member)]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/sessions/{sessionId:guid}/attendance")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class AttendanceController(AttendanceWorkflow workflow,
    IValidator<SaveAttendanceDraftRequest> draftValidator,
    IValidator<CorrectAttendanceRequest> correctionValidator) : TeachingControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<AttendanceSheetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, Guid sessionId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidActor();
        var result = await workflow.GetAsync(centerId, sessionId, Actor(actorId), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [HttpPut("draft"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<AttendanceSheetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveDraft(Guid centerId, Guid sessionId,
        [FromBody] SaveAttendanceDraftRequest request, CancellationToken token)
    {
        var validation = await draftValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetActor(out var actorId)) return InvalidActor();
        var result = await workflow.SaveDraftAsync(centerId, sessionId, request, Actor(actorId), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Attendance draft saved."))
            : Failure(result.Error);
    }

    [HttpPost("submit"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<AttendanceSheetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid centerId, Guid sessionId,
        [FromBody] SubmitAttendanceRequest request, CancellationToken token)
    {
        if (request.ExpectedVersion < 1) return BadRequest(ApiResponse<object>.Failure(
            [new ApiError("validation_error", "expectedVersion must be at least 1.", "expectedVersion")],
            HttpContext.TraceIdentifier, "Request validation failed."));
        if (!TryGetActor(out var actorId)) return InvalidActor();
        var result = await workflow.SubmitAsync(centerId, sessionId, request.ExpectedVersion, Actor(actorId), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Attendance submitted."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPatch("correction"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<AttendanceSheetDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Correct(Guid centerId, Guid sessionId,
        [FromBody] CorrectAttendanceRequest request, CancellationToken token)
    {
        var validation = await correctionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetActor(out var actorId)) return InvalidActor();
        var result = await workflow.CorrectAsync(centerId, sessionId, request, actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Attendance corrected."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpGet("history")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<AttendanceRevisionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> History(Guid centerId, Guid sessionId, CancellationToken token)
    {
        var result = await workflow.HistoryAsync(centerId, sessionId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    private AttendanceActor Actor(Guid userId) => new(userId,
        CenterAccessPolicyNames.IsManagementRole(HttpContext.Items[CenterAccessPolicyNames.RoleItemKey] as string));

    private UnauthorizedObjectResult InvalidActor() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")],
        HttpContext.TraceIdentifier, "Unauthorized."));
}
