using Asp.Versioning;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.Modules.Organizations.Contracts.Rooms;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Rooms;

[ApiController]
[ApiVersion(1.0)]
[Tags("06 · Rooms")]
[Route("api/v{version:apiVersion}/centers/{centerId:guid}/rooms")]
public sealed class RoomsController(
    IValidator<CreateRoomRequest> createValidator,
    IValidator<UpdateRoomRequest> updateValidator,
    IValidator<RoomVersionRequest> versionValidator,
    ICommandHandler<CreateRoomCommand, RoomDto> createHandler,
    ICommandHandler<UpdateRoomCommand, RoomDto> updateHandler,
    ICommandHandler<ArchiveRoomCommand, RoomDto> archiveHandler,
    ICommandHandler<ReactivateRoomCommand, RoomDto> reactivateHandler,
    IQueryHandler<GetRoomQuery, RoomDto> getHandler,
    IQueryHandler<ListRoomsQuery, List<RoomDto>> listHandler) : ControllerBase
{
    [Authorize(Policy = CenterAccessPolicyNames.Member)]
    [HttpGet]
    [ProducesResponseType<ApiResponse<OrganizationPage<RoomDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(Guid centerId, [FromQuery] bool includeArchived = false,
        [FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        // C5: paged like the other catalogs; a center has few rooms, so the list is paged in memory.
        if (page is < 1 or > 1_000_000 || pageSize is < 1 or > 100 || search?.Length > 100)
            return BadRequest(ApiResponse<object>.Failure([new ApiError("invalid_pagination",
                "page must be 1–1000000, pageSize 1–100 and search at most 100 characters.")],
                HttpContext.TraceIdentifier, "Invalid query."));
        var result = await listHandler.HandleAsync(new ListRoomsQuery(centerId, includeArchived), cancellationToken);
        var term = search?.Trim();
        var matches = string.IsNullOrEmpty(term) ? result.Value : result.Value.Where(x =>
            x.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            x.Code.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        return Ok(HttpContext.ToApiResponse(new OrganizationPage<RoomDto>
        {
            Page = page, PageSize = pageSize, TotalCount = matches.Count,
            Items = matches.Skip((page - 1) * pageSize).Take(pageSize).ToList()
        }));
    }

    [Authorize(Policy = CenterAccessPolicyNames.Member)]
    [HttpGet("{roomId:guid}")]
    [ProducesResponseType<ApiResponse<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid centerId, Guid roomId, CancellationToken cancellationToken)
    {
        var result = await getHandler.HandleAsync(new GetRoomQuery(centerId, roomId), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value)) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<RoomDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(Guid centerId, [FromBody] CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();

        var result = await createHandler.HandleAsync(new CreateRoomCommand(centerId, actorId,
            request.Name, request.LocationNote), cancellationToken);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, HttpContext.ToApiResponse(result.Value, "Room created."))
            : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPut("{roomId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid centerId, Guid roomId, [FromBody] UpdateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();

        var result = await updateHandler.HandleAsync(new UpdateRoomCommand(centerId, roomId, actorId,
            request.ExpectedVersion!.Value, request.Name, request.LocationNote), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Room updated.")) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost("{roomId:guid}/archive")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Archive(Guid centerId, Guid roomId, [FromBody] RoomVersionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await versionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();
        var result = await archiveHandler.HandleAsync(new ArchiveRoomCommand(centerId, roomId, actorId,
            request.ExpectedVersion!.Value), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Room archived.")) : MapFailure(result.Error);
    }

    [Authorize(Policy = CenterAccessPolicyNames.Management)]
    [HttpPost("{roomId:guid}/reactivate")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<RoomDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reactivate(Guid centerId, Guid roomId, [FromBody] RoomVersionRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await versionValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return ValidationFailure(validation.Errors);
        if (!TryGetUserId(out var actorId)) return AuthenticationFailure();
        var result = await reactivateHandler.HandleAsync(new ReactivateRoomCommand(centerId, roomId, actorId,
            request.ExpectedVersion!.Value), cancellationToken);
        return result.IsSuccess ? Ok(HttpContext.ToApiResponse(result.Value, "Room reactivated.")) : MapFailure(result.Error);
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
        if (error == RoomErrors.NotFound) return NotFound(response);
        if (error == RoomErrors.InvalidDetails) return BadRequest(response);
        // C7: 422 when the room or center state forbids the action; 409 for duplicates and races.
        if (error == RoomErrors.CenterNotReady || error == RoomErrors.ArchivedCannotEdit ||
            error == RoomErrors.HasFutureSessions)
            return UnprocessableEntity(response);
        return Conflict(response);
    }

    private ApiResponse<object> Failure(Error error) => ApiResponse<object>.Failure(
        [new ApiError(error.Code, error.Message, error.Field)], HttpContext.TraceIdentifier, error.Message);
}
