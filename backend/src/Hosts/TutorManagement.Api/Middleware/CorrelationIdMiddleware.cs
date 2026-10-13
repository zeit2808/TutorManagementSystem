using Microsoft.Extensions.Primitives;

namespace TutorManagement.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out StringValues correlationId) &&
            !StringValues.IsNullOrEmpty(correlationId))
        {
            context.TraceIdentifier = correlationId.ToString();
        }

        context.Response.Headers[HeaderName] = context.TraceIdentifier;
        await next(context);
    }
}
