using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TutorManagement.SharedKernel.Exceptions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.WebApi.Exceptions;

public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title, code) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, "Request validation failed", "validation_error"),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Authentication required", "unauthorized"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found", "not_found"),
            ConflictException => (StatusCodes.Status409Conflict, "Resource conflict", "conflict"),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Business rule violation", "business_rule_violation"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", "internal_error")
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(logger, httpContext.TraceIdentifier, exception);
        }
        else
        {
            LogHandledException(logger, httpContext.TraceIdentifier, exception);
        }

        var errors = exception is ApplicationBaseException applicationException && applicationException.Errors.Count > 0
            ? applicationException.Errors
                .Select(error => new ApiError(error.Code, error.Message, error.Field))
                .ToArray()
            : [new ApiError(code, statusCode == StatusCodes.Status500InternalServerError ? title : exception.Message)];

        httpContext.Response.StatusCode = statusCode;

        var response = ApiResponse<object>.Failure(errors, httpContext.TraceIdentifier);
        await httpContext.Response.WriteAsJsonAsync(
            response,
            cancellationToken: cancellationToken);

        return true;
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, string traceId, Exception exception);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "Handled request exception. TraceId: {TraceId}")]
    private static partial void LogHandledException(ILogger logger, string traceId, Exception exception);
}
