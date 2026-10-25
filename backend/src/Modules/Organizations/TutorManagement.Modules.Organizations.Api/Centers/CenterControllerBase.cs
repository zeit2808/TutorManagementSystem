using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.SharedKernel.Primitives;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.Modules.Organizations.Api.Centers;

public abstract class CenterControllerBase : ControllerBase
{
    protected bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirst("sub")?.Value, out userId);

    protected IActionResult ValidationFailure(ValidationResult validation) =>
        BadRequest(ApiResponse<object>.Failure(
            validation.Errors.Select(error =>
                new ApiError("validation_error", error.ErrorMessage, error.PropertyName)),
            HttpContext.TraceIdentifier,
            "Request validation failed."));

    protected IActionResult AuthenticationFailure() =>
        Unauthorized(Failure(new Error("unauthorized", "A valid access token is required.")));

    protected IActionResult MapFailure(Error error)
    {
        var response = Failure(error);
        if (error == CenterErrors.NotFound || error == CenterErrors.ChangeNotFound)
            return NotFound(response);
        // C7: 409 for duplicates and races, 422 for requests that break a business rule.
        if (error == CenterErrors.TaxCodeAlreadyExists || error == CenterErrors.ChangeAlreadyPending ||
            error == CenterErrors.CenterDetailsChanged)
            return Conflict(response);
        return UnprocessableEntity(response);
    }

    internal IActionResult Map(Error error) => MapFailure(error);

    private ApiResponse<object> Failure(Error error) =>
        ApiResponse<object>.Failure(
            [new ApiError(error.Code, error.Message, error.Field)],
            HttpContext.TraceIdentifier,
            error.Message);
}
