using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Teaching;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Teaching;

/// <summary>Maps M3 errors; everything else falls back to the shared teaching mapping.</summary>
public abstract class StudentAccessControllerBase : TeachingControllerBase
{
    protected IActionResult LinkFailure(Error error)
    {
        var response = ApiResponse<object>.Failure([new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier, error.Message);
        if (error == StudentLinkErrors.InviteLocked) return StatusCode(StatusCodes.Status429TooManyRequests, response);
        if (error == StudentLinkErrors.ProfileAlreadyLinked || error == StudentLinkErrors.AccountHasProfile ||
            error == StudentLinkErrors.MergeTwoAccounts || error == StudentLinkErrors.MergeSharedClass ||
            error == StudentLinkErrors.MergeSharedAttendance || error == ClassRegistrationErrors.AlreadyEnrolled ||
            error == ClassRegistrationErrors.ConcurrentChange) return Conflict(response);
        if (error == StudentLinkErrors.ActivationInvalid || error == StudentLinkErrors.InviteInvalid ||
            error == StudentLinkErrors.WrongCode || error == StudentLinkErrors.UseActivationLink ||
            error == StudentLinkErrors.ProfileUnavailable || error == StudentLinkErrors.NotLinked ||
            error == StudentLinkErrors.MergeInvalid || error == ClassRegistrationErrors.Full ||
            error == ClassRegistrationErrors.StudentArchived) return UnprocessableEntity(response);
        return Failure(error);
    }

    protected UnauthorizedObjectResult InvalidIdentity() => Unauthorized(ApiResponse<object>.Failure(
        [new ApiError("invalid_identity", "A valid user identity is required.")],
        HttpContext.TraceIdentifier, "Unauthorized."));
}

/// <summary>M3, center side: activation links for hand-made profiles, class invite links, unlink and merge.</summary>
[ApiController, ApiVersion(1.0), Tags("15 · Student Access")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}")]
[Authorize(Policy = CenterAccessPolicyNames.Management)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class StudentAccessController(StudentAccessWorkflow workflow) : StudentAccessControllerBase
{
    /// <summary>Creates a one-time link (14 days) for a profile without a login; older active links stop working.</summary>
    [HttpPost("students/{studentId:guid}/activation-link")]
    [ProducesResponseType<ApiResponse<ActivationLinkDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateActivationLink(Guid centerId, Guid studentId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.CreateActivationLinkAsync(centerId, studentId, actorId, token);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value))
            : LinkFailure(result.Error);
    }

    [HttpPost("students/{studentId:guid}/activation-link/revoke")]
    [ProducesResponseType<ApiResponse<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeActivationLink(Guid centerId, Guid studentId, CancellationToken token)
    {
        var result = await workflow.RevokeActivationLinksAsync(centerId, studentId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>One link for every profile of the class that has no login (the list to send to families).</summary>
    [HttpPost("classes/{classId:guid}/activation-links")]
    [ProducesResponseType<ApiResponse<List<ActivationLinkDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateClassActivationLinks(Guid centerId, Guid classId, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.CreateClassActivationLinksAsync(centerId, classId, actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value.ToList())) : LinkFailure(result.Error);
    }

    [HttpPost("students/{studentId:guid}/unlink-account"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnlinkAccount(Guid centerId, Guid studentId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        if (request.ExpectedVersion is not >= 0) return ExpectedVersionRequired();
        var result = await workflow.UnlinkAccountAsync(centerId, studentId, request.ExpectedVersion.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    [HttpPost("students/{studentId:guid}/dismiss-duplicate"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DismissDuplicate(Guid centerId, Guid studentId,
        [FromBody] TeachingVersionRequest request, CancellationToken token)
    {
        if (request.ExpectedVersion is not >= 0) return ExpectedVersionRequired();
        var result = await workflow.DismissDuplicateAsync(centerId, studentId, request.ExpectedVersion.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>"Gộp": moves the source profile's classes, attendance and login into this profile.</summary>
    [HttpPost("students/{studentId:guid}/merge"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<MergeResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Merge(Guid centerId, Guid studentId, [FromBody] MergeStudentRequest request,
        CancellationToken token)
    {
        var result = await workflow.MergeAsync(centerId, studentId, request.SourceStudentId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    [HttpGet("classes/{classId:guid}/invite-links")]
    [ProducesResponseType<ApiResponse<List<ClassInviteDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListInvites(Guid centerId, Guid classId, CancellationToken token)
    {
        var result = await workflow.ListInvitesAsync(centerId, classId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value.ToList())) : LinkFailure(result.Error);
    }

    [HttpPost("classes/{classId:guid}/invite-links"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassInviteDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateInvite(Guid centerId, Guid classId, [FromBody] CreateClassInviteRequest request,
        CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return InvalidIdentity();
        var result = await workflow.CreateInviteAsync(centerId, classId, request.ValidDays ?? 7, request.MaxUses,
            actorId, token);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value))
            : LinkFailure(result.Error);
    }

    [HttpPost("invite-links/{inviteId:guid}/revoke")]
    [ProducesResponseType<ApiResponse<ClassInviteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RevokeInvite(Guid centerId, Guid inviteId, CancellationToken token)
    {
        var result = await workflow.RevokeInviteAsync(centerId, inviteId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    [HttpPost("invite-links/{inviteId:guid}/recode")]
    [ProducesResponseType<ApiResponse<ClassInviteDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RecodeInvite(Guid centerId, Guid inviteId, CancellationToken token)
    {
        var result = await workflow.RecodeInviteAsync(centerId, inviteId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>Who joined through class links (newest first, at most 200).</summary>
    [HttpGet("class-invite-joins")]
    [ProducesResponseType<ApiResponse<List<ClassInviteJoinDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJoins(Guid centerId, [FromQuery] Guid? classId, CancellationToken token) =>
        Ok(HttpContext.ToApiResponse((await workflow.ListJoinsAsync(centerId, classId, token)).ToList()));

    private BadRequestObjectResult ExpectedVersionRequired() => BadRequest(ApiResponse<object>.Failure(
        [new ApiError("validation_error", "expectedVersion is required and must be zero or greater.", "expectedVersion")],
        HttpContext.TraceIdentifier, "Request validation failed."));
}

/// <summary>M3, student side: open a personal activation link or a class invite link while signed in.</summary>
[ApiController, ApiVersion(1.0), Tags("15 · Student Access")]
[Route("api/v{version:apiVersion}")]
[Authorize(Roles = "Student")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
public sealed class StudentJoinController(StudentJoinWorkflow workflow) : StudentAccessControllerBase
{
    [HttpGet("student-activation/{token}")]
    [ProducesResponseType<ApiResponse<ActivationPreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewActivation(string token, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await workflow.PreviewActivationAsync(token, userId, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>"Đúng là tôi": attaches this login to the link's profile.</summary>
    [HttpPost("student-activation/{token}/accept")]
    [ProducesResponseType<ApiResponse<ActivationResultDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptActivation(string token, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await workflow.AcceptActivationAsync(token, userId, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>"Không phải tôi": the link stops working and the center sees it reached the wrong person.</summary>
    [HttpPost("student-activation/{token}/decline")]
    [ProducesResponseType<ApiResponse<bool>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DeclineActivation(string token, CancellationToken cancellationToken)
    {
        var result = await workflow.DeclineActivationAsync(token, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    [HttpGet("class-invites/{token}")]
    [ProducesResponseType<ApiResponse<ClassInvitePreviewDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewInvite(string token, CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var result = await workflow.PreviewInviteAsync(token, userId, cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : LinkFailure(result.Error);
    }

    /// <summary>
    /// Joins the class with the code, pre-approved. 409 student_schedule_conflict lists clashes with the student's
    /// other classes at this center; resend with acknowledgeConflicts. 429 after five wrong codes (15 minutes).
    /// </summary>
    [HttpPost("class-invites/{token}/join"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassInviteJoinResultDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ScheduleCheckDto>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Join(string token, [FromBody] JoinClassInviteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActor(out var userId)) return InvalidIdentity();
        var (result, check) = await workflow.JoinAsync(token, userId, request.Code, request.FullName,
            request.ParentName, request.ParentPhone, request.StudiedHereBefore == true,
            request.AcknowledgeConflicts == true, cancellationToken);
        if (check is not null) return ScheduleConflict(check);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value))
            : LinkFailure(result.Error);
    }
}
