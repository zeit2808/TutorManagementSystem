using Asp.Versioning;
using FluentValidation;
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

public abstract class TeachingControllerBase : ControllerBase
{
    protected bool TryGetActor(out Guid actorId) => Guid.TryParse(User.FindFirst("sub")?.Value, out actorId);

    protected IActionResult InvalidPage() => BadRequest(ApiResponse<object>.Failure(
        [new ApiError("invalid_pagination", "page must be 1–1000000 and pageSize must be 1–100.")],
        HttpContext.TraceIdentifier, "Invalid pagination."));

    protected IActionResult InvalidSearch() => BadRequest(ApiResponse<object>.Failure(
        [new ApiError("validation_error", "search must be at most 100 characters.", "search")],
        HttpContext.TraceIdentifier, "Request validation failed."));

    protected static bool IsValidPage(int page, int pageSize) => page is >= 1 and <= 1_000_000 && pageSize is >= 1 and <= 100;

    protected IActionResult ValidationFailure(IEnumerable<FluentValidation.Results.ValidationFailure> errors) =>
        BadRequest(ApiResponse<object>.Failure(
            errors.Select(x => new ApiError("validation_error", x.ErrorMessage, x.PropertyName)),
            HttpContext.TraceIdentifier, "Request validation failed."));

    protected IActionResult Failure(Error error)
    {
        var response = ApiResponse<object>.Failure(
            [new ApiError(error.Code, error.Message, error.Field)], HttpContext.TraceIdentifier, error.Message);
        if (error == TeachingErrors.NotFound || error == TeachingErrors.NoActiveEnrollment ||
            error == StudentRequestErrors.NotFound || error == StudentRequestErrors.NoStudentProfile ||
            error == ScheduleChangeErrors.NoticeNotFound) return NotFound(response);
        if (error == TeachingErrors.AttendanceNotAssigned) return StatusCode(StatusCodes.Status403Forbidden, response);
        if (error == TeachingErrors.Invalid || error == TeachingErrors.InvalidSeries ||
            error == TeachingErrors.InvalidParentName || error == TeachingErrors.InvalidParentPhone ||
            error == TeachingErrors.InvalidWithdrawalReason || error == TeachingErrors.InvalidSessionReason ||
            error == TeachingErrors.NoOccurrences || error == TeachingErrors.ScheduleNotFuture ||
            error == TeachingErrors.ScheduleTooFar || error == TeachingErrors.InvalidScheduleCutoff ||
            error == TeachingErrors.InvalidAttendance || error == TeachingErrors.InvalidAttendanceRoster ||
            error == StudentRequestErrors.Invalid || error == StudentRequestErrors.InvalidReason ||
            error == ClassRegistrationErrors.InvalidClasses || error == ClassRegistrationErrors.InvalidSessionRange ||
            error.Code == "validation_error" ||
            error.Code == "invalid_schedule_local_time") return BadRequest(response);
        // C7: 409 only for races and duplicates; other refused actions break a business rule (422).
        if (error == TeachingErrors.ConcurrentChange || error == TeachingErrors.CodeExists ||
            error == TeachingErrors.AlreadyEnrolled || error == TeachingErrors.ScheduleConflict ||
            error == TeachingErrors.RequestIdReused || error == TeachingErrors.AccountInUse ||
            error == TeachingErrors.MakeupExists || error == TeachingErrors.AlreadyActive ||
            error == TeachingErrors.SeriesAlreadyStopped || error == ClassRegistrationErrors.ConcurrentChange ||
            error == TeachingErrors.StudentScheduleConflict || error == StudentRequestErrors.Duplicate ||
            error == StudentRequestErrors.ConcurrentChange)
            return Conflict(response);
        return UnprocessableEntity(response);
    }

    /// <summary>B5: 409 student_schedule_conflict with the clashes in <c>data</c> so the client can ask to confirm.</summary>
    protected IActionResult ScheduleConflict(ScheduleCheckDto check)
    {
        var error = TeachingErrors.StudentScheduleConflict;
        return Conflict(new ApiResponse<ScheduleCheckDto>
        {
            Success = false, Message = error.Message, Data = check, TraceId = HttpContext.TraceIdentifier,
            Errors = [new ApiError(error.Code, error.Message, error.Field)]
        });
    }

    protected Guid? ActorOrNull() => TryGetActor(out var actor) && actor != Guid.Empty ? actor : null;
}

[ApiController, ApiVersion(1.0), Tags("07 · Classes")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/classes")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class ClassesController(ClassCatalog classes, EnrollmentWorkflow enrollments,
    IValidator<CreateClassRequest> createValidator, IValidator<UpdateClassRequest> updateValidator,
    IValidator<TeachingVersionRequest> versionValidator, IValidator<EnrollStudentRequest> enrollValidator)
    : TeachingControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<TeachingPage<ClassDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] bool includeArchived = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (search?.Length > 100) return InvalidSearch();
        return Ok(HttpContext.ToApiResponse(await classes.ListAsync(centerId, includeArchived, search, page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{classId:guid}")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, Guid classId, CancellationToken token)
    {
        var result = await classes.GetAsync(centerId, classId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateClassRequest request, CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetActor(out var actorId)) return Unauthorized(ApiResponse<object>.Failure(
            [new ApiError("invalid_identity", "A valid user identity is required.")],
            HttpContext.TraceIdentifier, "Unauthorized."));
        var result = await classes.CreateAsync(centerId, request.SubjectId,
            request.Name, request.MaxStudents, actorId, token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Class created."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("{classId:guid}"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid centerId, Guid classId, [FromBody] UpdateClassRequest request,
        CancellationToken token)
    {
        var validation = await updateValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await classes.UpdateAsync(centerId, classId, request.Name,
            request.MaxStudents, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{classId:guid}/archive"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid centerId, Guid classId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await classes.ArchiveAsync(centerId, classId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{classId:guid}/reactivate"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<ClassDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid classId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await classes.ReactivateAsync(centerId, classId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{classId:guid}/enrollments")]
    [ProducesResponseType<ApiResponse<TeachingPage<ClassStudentDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListEnrollments(Guid centerId, Guid classId,
        [FromQuery] string status = "active", [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        var filter = status.ToLowerInvariant() switch
        {
            "active" => EnrollmentFilter.Active,
            "withdrawn" => EnrollmentFilter.Withdrawn,
            "all" => EnrollmentFilter.All,
            _ => (EnrollmentFilter?)null
        };
        if (!filter.HasValue)
            return BadRequest(ApiResponse<object>.Failure(
                [new ApiError("validation_error", "Status must be active, withdrawn or all.", "status")],
                HttpContext.TraceIdentifier, "Request validation failed."));
        var result = await enrollments.ListAsync(centerId, classId, filter.Value, page, pageSize, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    /// <summary>B5: clashes with the student's other classes here return 409 with the list; resend with acknowledgeConflicts.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{classId:guid}/enrollments"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<EnrollmentDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<ScheduleCheckDto>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Enroll(Guid centerId, Guid classId, [FromBody] EnrollStudentRequest request,
        CancellationToken token)
    {
        var validation = await enrollValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        TryGetActor(out var actorId);
        var (result, conflicts) = await enrollments.EnrollAsync(centerId, classId, request.StudentId,
            request.AcknowledgeConflicts == true, actorId == Guid.Empty ? null : actorId, token);
        if (conflicts is not null) return ScheduleConflict(conflicts);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Student enrolled."))
            : Failure(result.Error);
    }

    /// <summary>Decision D7: withdrawal takes effect now and needs a reason; past attendance is kept.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{classId:guid}/enrollments/{studentId:guid}/withdraw")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<EnrollmentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Withdraw(Guid centerId, Guid classId, Guid studentId,
        [FromBody] WithdrawStudentRequest request, CancellationToken token)
    {
        if (!TryGetActor(out var actorId)) return Unauthorized(ApiResponse<object>.Failure(
            [new ApiError("invalid_identity", "A valid user identity is required.")],
            HttpContext.TraceIdentifier, "Unauthorized."));
        var result = await enrollments.WithdrawAsync(centerId, classId, studentId, request.Reason ?? string.Empty,
            actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Student withdrawn.")) : Failure(result.Error);
    }
}

[ApiController, ApiVersion(1.0), Tags("08 · Students")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/students")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class StudentsController(PeopleCatalog people,
    IValidator<CreateStudentRequest> createValidator, IValidator<UpdateStudentRequest> updateValidator,
    IValidator<TeachingVersionRequest> versionValidator) : TeachingControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<TeachingPage<StudentDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] bool includeArchived = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (search?.Length > 100) return InvalidSearch();
        return Ok(HttpContext.ToApiResponse(await people.ListStudentsAsync(centerId, includeArchived, search, page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{studentId:guid}")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, Guid studentId, CancellationToken token)
    {
        var result = await people.GetStudentAsync(centerId, studentId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateStudentRequest request, CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.CreateStudentAsync(centerId, request.FullName,
            request.ParentName, request.ParentPhone, token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Student created."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("{studentId:guid}"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid centerId, Guid studentId, [FromBody] UpdateStudentRequest request,
        CancellationToken token)
    {
        var validation = await updateValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.UpdateStudentAsync(centerId, studentId, request.FullName,
            request.ParentName, request.ParentPhone, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{studentId:guid}/archive"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid centerId, Guid studentId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.ArchiveStudentAsync(centerId, studentId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{studentId:guid}/reactivate"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<StudentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid studentId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.ReactivateStudentAsync(centerId, studentId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }
}

[ApiController, ApiVersion(1.0), Tags("09 · Teachers")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/teachers")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class TeachersController(PeopleCatalog people,
    IValidator<CreateTeacherRequest> createValidator,
    IValidator<UpdateTeacherRequest> updateValidator,
    IValidator<TeachingVersionRequest> versionValidator) : TeachingControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<TeachingPage<TeacherDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] bool includeArchived = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken token = default)
    {
        if (!IsValidPage(page, pageSize)) return InvalidPage();
        if (search?.Length > 100) return InvalidSearch();
        return Ok(HttpContext.ToApiResponse(await people.ListTeachersAsync(centerId, includeArchived, search, page, pageSize, token)));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{teacherId:guid}")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid centerId, Guid teacherId, CancellationToken token)
    {
        var result = await people.GetTeacherAsync(centerId, teacherId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateTeacherRequest request, CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.CreateTeacherAsync(centerId, request.FullName, token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Teacher created."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("{teacherId:guid}"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid centerId, Guid teacherId, [FromBody] UpdateTeacherRequest request,
        CancellationToken token)
    {
        var validation = await updateValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.UpdateTeacherAsync(centerId, teacherId, request.FullName,
            request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{teacherId:guid}/archive"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid centerId, Guid teacherId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.ArchiveTeacherAsync(centerId, teacherId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{teacherId:guid}/reactivate"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<TeacherDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid teacherId, [FromBody] TeachingVersionRequest request,
        CancellationToken token)
    {
        var validation = await versionValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await people.ReactivateTeacherAsync(centerId, teacherId, request.ExpectedVersion!.Value, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : Failure(result.Error);
    }
}

[ApiController, ApiVersion(1.0), Tags("10 · Teaching Sessions")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/sessions")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
public sealed class TeachingSessionsController(SessionScheduler scheduler, ScheduleConflictChecker conflicts,
    ScheduleChangeQueries changes, ITeachingStore store,
    IValidator<CreateSessionRequest> createValidator,
    IValidator<RescheduleSessionRequest> rescheduleValidator,
    IValidator<SessionStatusChangeRequest> statusValidator) : TeachingControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet]
    [ProducesResponseType<ApiResponse<List<SessionDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] DateTimeOffset fromUtc,
        [FromQuery] DateTimeOffset toUtc, [FromQuery] Guid? classId = null, [FromQuery] Guid? teacherId = null,
        [FromQuery] Guid? roomId = null, CancellationToken token = default)
    {
        var result = await scheduler.ListAsync(centerId, fromUtc, toUtc, new SessionFilter(classId, teacherId, roomId), token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value.ToList())) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost, Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SessionDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateSessionRequest request, CancellationToken token)
    {
        var validation = await createValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        var result = await scheduler.ScheduleAsync(centerId, request.ClassId, request.TeacherId,
            request.RoomId, request.StartUtc, request.EndUtc, request.MakeupForSessionId, ActorOrNull(), token);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Session scheduled."))
            : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPut("{sessionId:guid}"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reschedule(Guid centerId, Guid sessionId,
        [FromBody] RescheduleSessionRequest request, CancellationToken token)
    {
        var validation = await rescheduleValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        TryGetActor(out var actorId);
        var result = await scheduler.RescheduleAsync(centerId, sessionId, request.TeacherId,
            request.RoomId, request.StartUtc, request.EndUtc, request.ExpectedVersion!.Value,
            request.Reason!, actorId == Guid.Empty ? null : actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Session rescheduled.")) : Failure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{sessionId:guid}/cancel"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SessionDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(Guid centerId, Guid sessionId, [FromBody] SessionStatusChangeRequest request,
        CancellationToken token)
    {
        var validation = await statusValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        TryGetActor(out var actorId);
        var result = await scheduler.CancelAsync(centerId, sessionId, request.ExpectedVersion!.Value, request.Reason!,
            actorId == Guid.Empty ? null : actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Session cancelled.")) : Failure(result.Error);
    }

    /// <summary>
    /// B5/B6: before adding or moving a session — how many students it affects, which of them have another class of
    /// this center then (names), how many clash elsewhere (count only), and whether it is urgent (within 24 hours).
    /// </summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("impact"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SessionImpactDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Impact(Guid centerId, [FromBody] SessionImpactRequest request, CancellationToken token)
    {
        if (request.ClassId == Guid.Empty || request.EndUtc <= request.StartUtc ||
            request.EndUtc - request.StartUtc > TimeSpan.FromHours(8))
            return ValidationFailure([new FluentValidation.Results.ValidationFailure("endUtc",
                "classId is required and the session must last more than 0 and at most 8 hours.")]);
        DateTimeOffset? originalStart = null;
        if (request.SessionId is { } sessionId)
        {
            var session = await store.FindSessionSnapshotAsync(centerId, sessionId, token);
            if (session is null || session.ClassId != request.ClassId) return Failure(TeachingErrors.NotFound);
            originalStart = session.StartUtc;
        }
        else if (await store.FindClassAsync(centerId, request.ClassId, token) is null) return Failure(TeachingErrors.NotFound);
        var impact = await conflicts.SessionImpactAsync(centerId, request.ClassId, request.StartUtc.ToUniversalTime(),
            request.EndUtc.ToUniversalTime(), request.SessionId, originalStart, token, request.TeacherId);
        return Ok(HttpContext.ToApiResponse(impact));
    }

    /// <summary>B6: the change log of one session — old and new time, reason, who and when (newest first).</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Member), HttpGet("{sessionId:guid}/changes")]
    [ProducesResponseType<ApiResponse<List<ScheduleChangeDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Changes(Guid centerId, Guid sessionId, CancellationToken token)
    {
        if (await store.FindSessionSnapshotAsync(centerId, sessionId, token) is null) return Failure(TeachingErrors.NotFound);
        return Ok(HttpContext.ToApiResponse((await changes.SessionHistoryAsync(centerId, sessionId, token)).ToList()));
    }

    /// <summary>D6: the session has started or ended but did not take place. A makeup can be scheduled after.</summary>
    [Authorize(Policy = CenterAccessPolicyNames.Management), HttpPost("{sessionId:guid}/not-held"), Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SessionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> NotHeld(Guid centerId, Guid sessionId, [FromBody] SessionStatusChangeRequest request,
        CancellationToken token)
    {
        var validation = await statusValidator.ValidateAsync(request, token);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        TryGetActor(out var actorId);
        var result = await scheduler.MarkNotHeldAsync(centerId, sessionId, request.ExpectedVersion!.Value,
            request.Reason!, actorId == Guid.Empty ? null : actorId, token);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Session recorded as not held.")) : Failure(result.Error);
    }
}
