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

[ApiController, ApiVersion(1.0), Tags("11 · Recurring Schedules")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/schedule-series")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class ScheduleSeriesController(RecurringScheduleWorkflow schedules,
    IValidator<CreateScheduleSeriesRequest> createValidator,
    IValidator<StopScheduleSeriesRequest> stopValidator,
    IValidator<ReplaceScheduleSeriesRequest> replaceValidator) : TeachingControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<TeachingPage<ScheduleSeriesDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] Guid? classId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        return Ok(HttpContext.ToApiResponse(await schedules.ListAsync(centerId, classId, page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{seriesId:guid}")]
    [ProducesResponseType<ApiResponse<ScheduleSeriesDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, Guid seriesId, CancellationToken token)
    {
        var result = await schedules.GetAsync(centerId, seriesId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("preview"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SchedulePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid centerId, [FromBody] CreateScheduleSeriesRequest request,
        CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await schedules.PreviewAsync(centerId, request, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ScheduleSeriesCreatedDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ScheduleSeriesCreatedDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateScheduleSeriesRequest request,
        CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await schedules.CreateAsync(centerId, request, token);
        if (result.IsFailure) return Failure(result.Error);
        return result.Value.Replayed
            ? Ok(HttpContext.ToApiResponse(result.Value, "Schedule request replayed."))
            : StatusCode(StatusCodes.Status201Created,
                HttpContext.ToApiResponse(result.Value, "Recurring schedule created."));
    }

    /// <summary>B6: reason required; under 7 days before the first affected session needs acknowledgeShortNotice (else 422 schedule_short_notice).</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{seriesId:guid}/stop"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ScheduleSeriesStoppedDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Stop(Guid centerId, Guid seriesId,
        [FromBody] StopScheduleSeriesRequest request, CancellationToken token)
    {
        var validation = await stopValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await schedules.StopAsync(centerId, seriesId, request.FromDate,
            request.ExpectedVersion!.Value, request.Reason!, request.AcknowledgeShortNotice == true, ActorOrNull(), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Future sessions cancelled."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{seriesId:guid}/replace-future"),
        Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ScheduleSeriesReplacedDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReplaceFuture(Guid centerId, Guid seriesId,
        [FromBody] ReplaceScheduleSeriesRequest request, CancellationToken token)
    {
        var validation = await replaceValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await schedules.ReplaceFutureAsync(centerId, seriesId, request, ActorOrNull(), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Future schedule replaced."))
            : Failure(result.Error);
    }
}
