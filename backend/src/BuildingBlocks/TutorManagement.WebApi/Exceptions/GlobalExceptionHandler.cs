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
        // The browser went away (page reload, tab closed, request superseded): nothing failed and nobody is
        // listening for the answer. Not an error — no 500, no error log, no body.
        if (httpContext.RequestAborted.IsCancellationRequested && IsClientDisconnect(exception))
        {
            LogClientAborted(logger, httpContext.TraceIdentifier, httpContext.Request.Method, httpContext.Request.Path);
            if (!httpContext.Response.HasStarted)
            {
                httpContext.Response.StatusCode = StatusCodeClientClosedRequest;
            }

            return true;
        }

        var (statusCode, title, code) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, "Request validation failed", "validation_error"),
            BadHttpRequestException badRequest => (badRequest.StatusCode, "Bad request", "bad_request"),
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

        var response = ApiResponse<object>.Failure(errors, httpContext.TraceIdentifier, title);
        await httpContext.Response.WriteAsJsonAsync(
            response,
            cancellationToken: cancellationToken);

        return true;
    }

    /// <summary>
    /// What a request throws when the client hangs up mid-way: cancelled awaits, a body that stops arriving,
    /// a reset connection.
    /// </summary>
    private static bool IsClientDisconnect(Exception exception) =>
        exception is OperationCanceledException or BadHttpRequestException or IOException ||
        exception.InnerException is { } inner && IsClientDisconnect(inner);

    /// <summary>Nginx's de-facto code for "client closed request"; only shows up in logs and traces.</summary>
    private const int StatusCodeClientClosedRequest = 499;

    [LoggerMessage(EventId = 4990, Level = LogLevel.Debug,
        Message = "Request cancelled by the client. TraceId: {TraceId} {Method} {Path}")]
    private static partial void LogClientAborted(ILogger logger, string traceId, string method, PathString path);

    [LoggerMessage(EventId = 5000, Level = LogLevel.Error, Message = "Unhandled exception. TraceId: {TraceId}")]
    private static partial void LogUnhandledException(ILogger logger, string traceId, Exception exception);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "Handled request exception. TraceId: {TraceId}")]
    private static partial void LogHandledException(ILogger logger, string traceId, Exception exception);
}
