using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.ApproveCenter;
using TutorManagement.Modules.Organizations.Application.Centers.CreateCenter;
using TutorManagement.Modules.Organizations.Application.Centers.ReinstateCenter;
using TutorManagement.Modules.Organizations.Application.Centers.RejectCenter;
using TutorManagement.Modules.Organizations.Application.Centers.SuspendCenter;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

[ApiController]
[ApiVersion(1.0)]
[Tags("90 · Platform centers")]
[Authorize(Roles = "PlatformAdmin")]
[Route("api/v{version:apiVersion}/platform/centers")]
public sealed class PlatformCentersController(
    IValidator<CreateCenterRequest> createValidator,
    IValidator<ReviewCenterRequest> reviewValidator,
    ICommandHandler<CreateCenterCommand, CenterDto> createHandler,
    ICommandHandler<ApproveCenterCommand, CenterDto> approveHandler,
    ICommandHandler<RejectCenterCommand, CenterDto> rejectHandler,
    ICommandHandler<SuspendCenterCommand, CenterDto> suspendHandler,
    ICommandHandler<ReinstateCenterCommand, CenterDto> reinstateHandler,
    CenterLifecycleWorkflow lifecycle)
    : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(Failure<object>(
                validation.Errors.Select(error => new Error(
                    "validation_error", error.ErrorMessage, error.PropertyName)),
                "Request validation failed."));
        }

        var result = await createHandler.HandleAsync(
            new CreateCenterCommand(
                request.LegalName,
                request.DisplayName,
                request.TaxCode,
                request.BusinessRegistrationNumber,
                request.Address,
                request.ContactEmail,
                request.ContactPhone),
            cancellationToken);

        if (result.IsFailure)
        {
            return Conflict(Failure<object>([result.Error], "Center creation failed."));
        }

        return StatusCode(
            StatusCodes.Status201Created,
            HttpContext.ToApiResponse(result.Value, "Center submitted for approval."));
    }

    /// <summary>Centers newest first, filtered by status and by display/legal name or tax-code prefix.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<OrganizationPage<CenterDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status = null, [FromQuery] string? search = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100 || search?.Length > 100)
            return BadRequest(Failure<object>([new Error("invalid_pagination",
                "page must be 1–1000000, pageSize 1–100 and search at most 100 characters.")], "Invalid query."));
        CenterStatus? parsed = null;
        if (status is not null)
        {
            if (!Enum.TryParse<CenterStatus>(status, true, out var value) || !Enum.IsDefined(value))
                return BadRequest(Failure<object>([new Error("validation_error",
                    "status must be PendingApproval, Approved, Rejected or Suspended.", "status")], "Invalid query."));
            parsed = value;
        }
        return Ok(HttpContext.ToApiResponse(await lifecycle.ListAsync(parsed, search, page, pageSize, cancellationToken)));
    }

    [HttpPost("{centerId:guid}/approve")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid centerId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var reviewerId))
        {
            return Unauthorized(Failure<object>(
                [new Error("unauthorized", "A valid access token is required.")],
                "Authentication required."));
        }

        var result = await approveHandler.HandleAsync(
            new ApproveCenterCommand(centerId, reviewerId), cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(HttpContext.ToApiResponse(result.Value, "Center approved."));
        }

        return result.Error == CenterErrors.NotFound
            ? NotFound(Failure<object>([result.Error], "Center was not found."))
            : Conflict(Failure<object>([result.Error], "Center approval failed."));
    }

    [HttpPost("{centerId:guid}/reject")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        Guid centerId,
        [FromBody] ReviewCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateReviewAsync(request, cancellationToken);
        if (validationError is not null) return validationError;
        if (!TryGetUserId(out var reviewerId)) return MissingIdentity();

        var result = await rejectHandler.HandleAsync(
            new RejectCenterCommand(centerId, reviewerId, request.Reason), cancellationToken);
        return ReviewResult(result, "Center rejected.");
    }

    [HttpPost("{centerId:guid}/suspend")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Suspend(
        Guid centerId,
        [FromBody] ReviewCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateReviewAsync(request, cancellationToken);
        if (validationError is not null) return validationError;
        if (!TryGetUserId(out var reviewerId)) return MissingIdentity();

        var result = await suspendHandler.HandleAsync(
            new SuspendCenterCommand(centerId, reviewerId, request.Reason), cancellationToken);
        return ReviewResult(result, "Center suspended.");
    }

    [HttpPost("{centerId:guid}/reinstate")]
    [ProducesResponseType<ApiResponse<CenterDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reinstate(
        Guid centerId,
        [FromBody] ReviewCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateReviewAsync(request, cancellationToken);
        if (validationError is not null) return validationError;
        if (!TryGetUserId(out var reviewerId)) return MissingIdentity();

        var result = await reinstateHandler.HandleAsync(
            new ReinstateCenterCommand(centerId, reviewerId, request.Reason), cancellationToken);
        return ReviewResult(result, "Center reinstated.");
    }

    private async Task<IActionResult?> ValidateReviewAsync(
        ReviewCenterRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await reviewValidator.ValidateAsync(request, cancellationToken);
        return validation.IsValid
            ? null
            : BadRequest(Failure<object>(
                validation.Errors.Select(error => new Error(
                    "validation_error", error.ErrorMessage, error.PropertyName)),
                "Request validation failed."));
    }

    private IActionResult ReviewResult(Result<CenterDto> result, string successMessage)
    {
        if (result.IsSuccess)
        {
            return Ok(HttpContext.ToApiResponse(result.Value, successMessage));
        }

        return result.Error == CenterErrors.NotFound
            ? NotFound(Failure<object>([result.Error], "Center was not found."))
            : Conflict(Failure<object>([result.Error], "Center review failed."));
    }

    private UnauthorizedObjectResult MissingIdentity() => Unauthorized(Failure<object>(
        [new Error("unauthorized", "A valid access token is required.")],
        "Authentication required."));

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out userId);

    private ApiResponse<T> Failure<T>(IEnumerable<Error> errors, string message) =>
        ApiResponse<T>.Failure(
            errors.Select(error => new ApiError(error.Code, error.Message, error.Field)),
            HttpContext.TraceIdentifier,
            message);
}
