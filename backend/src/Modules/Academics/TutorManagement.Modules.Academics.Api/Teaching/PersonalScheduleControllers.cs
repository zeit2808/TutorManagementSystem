using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

/// <summary>
/// B4/B7/B8: what belongs to the signed-in person across every center — their schedule (as student and teacher),
/// schedule-change notices and requests. Nobody else can read these; centers see only their own part.
/// </summary>
[ApiController, ApiVersion(1.0), Tags("13 · My Schedule"), Authorize]
[Route("api/v{version:apiVersion}/me")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
public sealed class MyScheduleController(PersonalScheduleWorkflow schedule, ScheduleChangeQueries notices,
    StudentRequestWorkflow requests) : TeachingControllerBase
{
    /// <summary>Sessions of every center in [fromUtc, toUtc) (at most 42 days), with status, reasons and overlaps.</summary>
    [HttpGet("schedule")]
    [ProducesResponseType<ApiResponse<MyScheduleDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Schedule([FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc,
        CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized(InvalidIdentityResponse());
        var result = await schedule.GetAsync(userId, fromUtc, toUtc, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>Schedule-change notices of the student's classes, newest first, with the unread count.</summary>
    [HttpGet("notices")]
    [ProducesResponseType<ApiResponse<MyNoticePage>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Notices([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (!TryGetActor(out var userId)) return Unauthorized(InvalidIdentityResponse());
        return Ok(HttpContext.ToApiResponse(await notices.MyNoticesAsync(userId, unreadOnly, page, pageSize, token)));
    }

    /// <summary>"Đã xem, vẫn đi được".</summary>
    [HttpPost("notices/{noticeId:guid}/acknowledge")]
    [ProducesResponseType<ApiResponse<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Acknowledge(Guid noticeId, CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized(InvalidIdentityResponse());
        var result = await notices.AcknowledgeAsync(userId, noticeId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>Marks every notice as seen (without answering them).</summary>
    [HttpPost("notices/seen")]
    [ProducesResponseType<ApiResponse<int>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllSeen(CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized(InvalidIdentityResponse());
        return Ok(HttpContext.ToApiResponse(await notices.MarkAllSeenAsync(userId, token)));
    }

    /// <summary>The student's own requests in every center, newest first.</summary>
    [HttpGet("requests")]
    [ProducesResponseType<ApiResponse<TeachingPage<StudentRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Requests([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (!TryGetActor(out var userId)) return Unauthorized(InvalidIdentityResponse());
        return Ok(HttpContext.ToApiResponse(await requests.MineAsync(userId, page, pageSize, token)));
    }

    private ApiResponse<object> InvalidIdentityResponse() => ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")], HttpContext.TraceIdentifier, "Unauthorized.");
}

/// <summary>B8/F6: student requests (absence, makeup, transfer, withdrawal) and the center's review queue.</summary>
[ApiController, ApiVersion(1.0), Tags("14 · Student Requests")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class StudentRequestsController(StudentRequestWorkflow workflow, ScheduleChangeQueries changes)
    : TeachingControllerBase
{
    /// <summary>
    /// A student's request about one of their classes. Absence (before the session starts) and makeup name a
    /// session; transfer names a target class of this center. Link noticeId when answering a schedule notice.
    /// </summary>
    [Authorize(Roles = "Student"), HttpPost("my/requests"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentRequestDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Submit(Guid centerId, [FromBody] SubmitStudentRequestRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized();
        var result = await workflow.SubmitAsync(centerId, userId, new SubmitStudentRequestInput(request.Kind ?? "",
            request.ClassId, request.SessionId, request.TargetClassId, request.NoticeId, request.Reason ?? ""), token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Request sent."))
            : Failure(result.Error);
    }

    [Authorize(Roles = "Student"), HttpPost("my/requests/{requestId:guid}/cancel")]
    [ProducesResponseType<ApiResponse<StudentRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid centerId, Guid requestId, CancellationToken token)
    {
        if (!TryGetActor(out var userId)) return Unauthorized();
        var result = await workflow.CancelAsync(centerId, userId, requestId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>Review queue. status: Pending (oldest first), Approved, Rejected, Cancelled; kind and classId narrow it.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("student-requests")]
    [ProducesResponseType<ApiResponse<TeachingPage<StudentRequestDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] string? status = null, [FromQuery] string? kind = null,
        [FromQuery] Guid? classId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        StudentRequestStatus? parsedStatus = null;
        StudentRequestKind? parsedKind = null;
        if (status is not null)
        {
            if (!Enum.TryParse<StudentRequestStatus>(status, true, out var value) || !Enum.IsDefined(value) || int.TryParse(status, out _))
                return ValidationFailure([new FluentValidation.Results.ValidationFailure("status",
                    "status must be Pending, Approved, Rejected or Cancelled.")]);
            parsedStatus = value;
        }
        if (kind is not null)
        {
            if (!Enum.TryParse<StudentRequestKind>(kind, true, out var value) || !Enum.IsDefined(value) || int.TryParse(kind, out _))
                return ValidationFailure([new FluentValidation.Results.ValidationFailure("kind",
                    "kind must be ExcusedAbsence, Makeup, Transfer or Withdrawal.")]);
            parsedKind = value;
        }
        return Ok(HttpContext.ToApiResponse(await workflow.ListAsync(centerId, parsedStatus, parsedKind, classId, page,
            pageSize, token)));
    }

    /// <summary>
    /// Approve. Absence: fills "Vắng có phép" on the attendance sheet. Withdrawal: ends the enrollment now.
    /// Transfer: moves the student in one transaction; clashes in this center return 409 with the list unless
    /// acknowledgeConflicts is set. Makeup: records the decision; schedule the makeup session separately.
    /// </summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("student-requests/{requestId:guid}/approve")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ScheduleCheckDto>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid centerId, Guid requestId, [FromBody] ApproveStudentRequestRequest request,
        CancellationToken token)
    {
        if (request.ExpectedVersion is not >= 0)
            return ValidationFailure([new FluentValidation.Results.ValidationFailure(
                "ExpectedVersion", "expectedVersion is required and must be zero or greater.")]);
        if (!TryGetActor(out var reviewerId)) return Unauthorized();
        var (result, check) = await workflow.ApproveAsync(centerId, requestId, reviewerId, request.Note,
            request.ExpectedVersion.Value, request.AcknowledgeConflicts == true, token);
        if (check is not null) return ScheduleConflict(check);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("student-requests/{requestId:guid}/reject")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentRequestDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid centerId, Guid requestId, [FromBody] RejectStudentRequestRequest request,
        CancellationToken token)
    {
        if (request.ExpectedVersion is not >= 0)
            return ValidationFailure([new FluentValidation.Results.ValidationFailure(
                "ExpectedVersion", "expectedVersion is required and must be zero or greater.")]);
        if (!TryGetActor(out var reviewerId)) return Unauthorized();
        var result = await workflow.RejectAsync(centerId, requestId, reviewerId, request.Reason ?? "",
            request.ExpectedVersion.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>F6: recent schedule changes with how students answered (seen, acknowledged, requests by kind).</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpGet("schedule-changes")]
    [ProducesResponseType<ApiResponse<TeachingPage<ScheduleChangeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Changes(Guid centerId, [FromQuery] Guid? classId = null, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20, CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        return Ok(HttpContext.ToApiResponse(await changes.CenterChangesAsync(centerId, classId, page, pageSize, token)));
    }
}
