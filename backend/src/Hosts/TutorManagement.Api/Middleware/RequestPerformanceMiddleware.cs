using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace TutorManagement.Api.Middleware;

public sealed class RequestPerformanceMiddleware(
    RequestDelegate next,
    ILogger<RequestPerformanceMiddleware> logger,
    IOptions<RequestPerformanceOptions> options)
{
    private readonly RequestPerformanceOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();

        context.Response.OnStarting(() =>
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            context.Response.Headers["Server-Timing"] = string.Create(
                CultureInfo.InvariantCulture,
                $"app;dur={elapsed.TotalMilliseconds:0.##}");

            return Task.CompletedTask;
        });

        var faulted = false;
        try
        {
            await next(context);
        }
        catch
        {
            faulted = true;
            throw;
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt);
            var login = context.Request.Path.Value?.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase) == true;
            var threshold = login
                ? _options.LoginSlowRequestThresholdMilliseconds
                : _options.SlowRequestThresholdMilliseconds;
            if (elapsed.TotalMilliseconds >= threshold)
            {
                var stages = login ? GetLoginStageTimings() : string.Empty;
                RequestPerformanceLog.SlowRequest(
                    logger,
                    context.Request.Method,
                    context.Request.Path,
                    faulted && !context.Response.HasStarted
                        ? StatusCodes.Status500InternalServerError
                        : context.Response.StatusCode,
                    elapsed.TotalMilliseconds,
                    threshold,
                    stages,
                    context.TraceIdentifier);
            }
        }
    }

    private static string GetLoginStageTimings()
    {
        var stages = Activity.Current?.TagObjects
            .Where(tag => tag.Key.StartsWith("login.stage.", StringComparison.Ordinal))
            .Select(tag => $"{tag.Key[12..]}={Convert.ToString(tag.Value, CultureInfo.InvariantCulture)}")
            .ToArray();
        return stages is { Length: > 0 } ? string.Join(", ", stages) : "unavailable";
    }
}

internal static partial class RequestPerformanceLog
{
    [LoggerMessage(
        EventId = 3000,
        Level = LogLevel.Warning,
        Message = "Slow HTTP request detected: {Method} {Path} returned {StatusCode} in {ElapsedMilliseconds} ms (threshold {ThresholdMilliseconds} ms; login stages: {LoginStages}). TraceId: {TraceId}")]
    internal static partial void SlowRequest(
        ILogger logger,
        string method,
        PathString path,
        int statusCode,
        double elapsedMilliseconds,
        int thresholdMilliseconds,
        string loginStages,
        string traceId);
}
