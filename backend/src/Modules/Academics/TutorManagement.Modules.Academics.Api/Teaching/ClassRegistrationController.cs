using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

[ApiController, ApiVersion(1.0), Tags("11 · Class Registration")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
public sealed class ClassRegistrationController(ClassRegistrationWorkflow workflow, ScheduleConflictChecker conflicts)
    : TeachingControllerBase
{
    /// <summary>
    /// F3: before submitting, which sessions of the chosen classes clash with the student's other classes (any
    /// center, named — it is their own schedule) or with each other. Nothing is blocked; the student decides.
    /// </summary>
    [Authorize(Roles = "Student"), HttpPost("class-applications/check"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ApplicationCheckDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Check(Guid centerId, [FromBody] CheckClassApplicationsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await conflicts.CheckApplicationAsync(centerId, userId, request.ClassIds ?? [], cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    [Authorize(Roles = "Student"), HttpGet("open-classes")]
    [ProducesResponseType<ApiResponse<TeachingPage<OpenClassDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> OpenClasses(Guid centerId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        var result = await workflow.OpenClassesAsync(centerId, page, pageSize, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    [Authorize(Roles = "Student"), HttpPost("class-applications"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<List<ClassApplicationDto>>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<List<ClassApplicationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit(Guid centerId, [FromBody] SubmitClassApplicationsRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await workflow.SubmitAsync(centerId, userId, request.FullName, request.ParentName ?? string.Empty,
            request.ParentPhone ?? string.Empty, request.ClassIds, idempotencyKey ?? string.Empty, cancellationToken);
        if (result.IsFailure) return Fail(result.Error);
        return result.Value.Replayed
            ? Ok(HttpContext.ToApiResponse(result.Value.Items.ToList(), "Existing submission returned."))
            : StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value.Items.ToList()));
    }

    [Authorize(Roles = "Student"), HttpGet("my/class-applications")]
    [ProducesResponseType<ApiResponse<TeachingPage<ClassApplicationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyApplications(Guid centerId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await workflow.MyApplicationsAsync(centerId, userId, page, pageSize, cancellationToken);
        return Ok(HttpContext.ToApiResponse(result));
    }

    [Authorize(Roles = "Student"), HttpGet("my/classes")]
    [ProducesResponseType<ApiResponse<List<OpenClassDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MyClasses(Guid centerId, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        return Ok(HttpContext.ToApiResponse((await workflow.MyClassesAsync(centerId, userId,
            cancellationToken)).ToList()));
    }

    [Authorize(Roles = "Student"), HttpGet("my/sessions")]
    [ProducesResponseType<ApiResponse<List<StudentSessionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MySessions(Guid centerId, [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        if (fromUtc == default || toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(93))
            return Fail(ClassRegistrationErrors.InvalidSessionRange);
        return Ok(HttpContext.ToApiResponse((await workflow.MySessionsAsync(centerId, userId,
            fromUtc, toUtc, cancellationToken)).ToList()));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("classes/{classId:guid}/registration")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetRegistration(Guid centerId, Guid classId,
        [FromBody] SetClassRegistrationRequest request, CancellationToken cancellationToken)
    {
        if (request.ExpectedVersion is not >= 0)
            return ValidationFailure([new FluentValidation.Results.ValidationFailure(
                "ExpectedVersion", "expectedVersion is required and must be zero or greater.")]);
        var result = await workflow.SetRegistrationAsync(centerId, classId, request.Open,
            request.ExpectedVersion!.Value, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("class-applications")]
    [ProducesResponseType<ApiResponse<TeachingPage<ClassApplicationDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListApplications(Guid centerId, [FromQuery] string? status = null,
        [FromQuery] Guid? classId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        ClassApplicationStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<ClassApplicationStatus>(status, true, out var parsed) ||
                !Enum.IsDefined(parsed)) return Fail(ClassRegistrationErrors.InvalidStatus);
            parsedStatus = parsed;
        }
        var result = await workflow.ListApplicationsAsync(centerId, parsedStatus, classId, page, pageSize, cancellationToken);
        return Ok(HttpContext.ToApiResponse(result));
    }

    /// <summary>B5: the body is optional; send {"acknowledgeConflicts": true} after a 409 student_schedule_conflict.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("class-applications/{applicationId:guid}/approve")]
    [ProducesResponseType<ApiResponse<ClassApplicationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ScheduleCheckDto>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid centerId, Guid applicationId,
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] ApproveClassApplicationRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var reviewerId)) return InvalidIdentity();
        var (result, check) = await workflow.ApproveAsync(centerId, applicationId, reviewerId,
            request?.AcknowledgeConflicts == true, cancellationToken);
        if (check is not null) return ScheduleConflict(check);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("class-applications/{applicationId:guid}/reject")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassApplicationDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid centerId, Guid applicationId,
        [FromBody] RejectClassApplicationRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var reviewerId)) return InvalidIdentity();
        var result = await workflow.RejectAsync(centerId, applicationId, reviewerId,
            request.Reason, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Fail(result.Error);
    }

    private IActionResult Fail(Error error)
    {
        var invalidRequest = error == ClassRegistrationErrors.Invalid || error.Code == "validation_error";
        var response = ApiResponse<object>.Failure(
            [new ApiError(error.Code, error.Message, error.Field)], HttpContext.TraceIdentifier,
            invalidRequest ? "Request validation failed." : error.Message);
        if (error == ClassRegistrationErrors.NotFound) return NotFound(response);
        if (invalidRequest) return BadRequest(response);
        // C7: duplicates and races are 409; closed classes, full classes and reviewed applications are 422.
        if (error == ClassRegistrationErrors.AlreadyApplied || error == ClassRegistrationErrors.KeyReused ||
            error == ClassRegistrationErrors.AlreadyEnrolled || error == ClassRegistrationErrors.ConcurrentChange)
            return Conflict(response);
        return UnprocessableEntity(response);
    }

    private UnauthorizedObjectResult InvalidIdentity() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")],
        HttpContext.TraceIdentifier, "Unauthorized."));
}
