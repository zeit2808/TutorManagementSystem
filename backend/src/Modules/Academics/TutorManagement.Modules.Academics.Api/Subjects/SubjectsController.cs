using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Subjects.ArchiveSubject;
using TutorManagement.Modules.Academics.Application.Subjects.CreateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.GetSubject;
using TutorManagement.Modules.Academics.Application.Subjects.ListSubjects;
using TutorManagement.Modules.Academics.Application.Subjects.ReactivateSubject;
using TutorManagement.Modules.Academics.Application.Subjects.UpdateSubject;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Academics.Contracts.Subjects;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Academics.Api.Subjects;

[ApiController]
[ApiVersion(1.0)]
[Tags("05 · Subjects")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/subjects")]
public sealed class SubjectsController(
    IValidator<CreateSubjectRequest> createValidator,
    IValidator<UpdateSubjectRequest> updateValidator,
    IValidator<SubjectVersionRequest> versionValidator,
    ICommandHandler<CreateSubjectCommand, SubjectDto> createHandler,
    ICommandHandler<UpdateSubjectCommand, SubjectDto> updateHandler,
    ICommandHandler<ArchiveSubjectCommand, SubjectDto> archiveHandler,
    ICommandHandler<ReactivateSubjectCommand, SubjectDto> reactivateHandler,
    IQueryHandler<GetSubjectQuery, SubjectDto> getHandler,
    IQueryHandler<ListSubjectsQuery, List<SubjectDto>> listHandler) : ControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member)]
    [HttpGet]
    [ProducesResponseType<ApiResponse<TeachingPage<SubjectDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] bool includeArchived = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        // C5: same paging shape as classes, students and teachers. A center has few subjects, so the
        // filtered list is paged in memory.
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100 || search?.Length > 100)
            return BadRequest(ApiResponse<object>.Failure([new ApiError("invalid_pagination",
                "page must be 1–1000000, pageSize 1–100 and search at most 100 characters.")],
                HttpContext.TraceIdentifier, "Invalid query."));
        var result = await listHandler.HandleAsync(new ListSubjectsQuery(centerId, includeArchived), cancellationToken);
        var term = search?.Trim();
        var matches = string.IsNullOrEmpty(term) ? result.Value : result.Value.Where(x =>
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Code.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(HttpContext.ToApiResponse(new TeachingPage<SubjectDto>
        {
            Page = page, PageSize = pageSize, TotalCount = matches.Count,
            Items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList()
        }));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member)]
    [HttpGet("{subjectId:guid}")]
    [ProducesResponseType<ApiResponse<SubjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid centerId, Guid subjectId, CancellationToken cancellationToken)
    {
        var result = await getHandler.HandleAsync(new GetSubjectQuery(centerId, subjectId), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SubjectDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateSubjectRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();

        var result = await createHandler.HandleAsync(
            new CreateSubjectCommand(centerId, actorId, request.Code, request.Name, request.Description),
            cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Subject created."))
            : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPut("{subjectId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SubjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid centerId, Guid subjectId, [FromBody] UpdateSubjectRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();

        var result = await updateHandler.HandleAsync(
            new UpdateSubjectCommand(centerId, subjectId, actorId, request.ExpectedVersion!.Value,
                request.Code, request.Name, request.Description),
            cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Subject updated.")) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost("{subjectId:guid}/archive")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SubjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(Guid centerId, Guid subjectId,
        [FromBody] SubjectVersionRequest request, CancellationToken cancellationToken)
    {
        var validation = await versionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();
        var result = await archiveHandler.HandleAsync(
            new ArchiveSubjectCommand(centerId, subjectId, actorId, request.ExpectedVersion!.Value), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Subject archived.")) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost("{subjectId:guid}/reactivate")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<SubjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid subjectId,
        [FromBody] SubjectVersionRequest request, CancellationToken cancellationToken)
    {
        var validation = await versionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();
        var result = await reactivateHandler.HandleAsync(
            new ReactivateSubjectCommand(centerId, subjectId, actorId, request.ExpectedVersion!.Value), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Subject reactivated.")) : MapFailure(result.Error);
    }

    private bool TryGetUserId(out Guid userId) => Guid.TryParse(User.FindFirst("sub")?.Value, out userId);

    private BadRequestObjectResult ValidationFailure(IEnumerable<FluentValidation.Results.ValidationFailure> errors) =>
        BadRequest(ApiResponse<object>.Failure(
            errors.Select(error => new ApiError("validation_error", error.ErrorMessage, error.PropertyName)),
            HttpContext.TraceIdentifier, "Request validation failed."));

    private UnauthorizedObjectResult AuthenticationFailure() => Unauthorized(Failure(
        new Error("unauthorized", "A valid access token is required.")));

    private IActionResult MapFailure(Error error)
    {
        var response = Failure(error);
        if (error == SubjectErrors.NotFound) return NotFound(response);
        if (error == SubjectErrors.InvalidDetails) return BadRequest(response);
        // C7: 422 when the subject's state forbids the action; 409 for duplicates and races.
        if (error == SubjectErrors.HasActiveClasses || error == SubjectErrors.ArchivedCannotEdit)
            return UnprocessableEntity(response);
        return Conflict(response);
    }

    private ApiResponse<object> Failure(Error error) => ApiResponse<object>.Failure(
        [new ApiError(error.Code, error.Message, error.Field)], HttpContext.TraceIdentifier, error.Message);
}
