using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TutorManagement.Api.Middleware;

namespace TutorManagement.IntegrationTests;

public sealed class RequestPerformanceMiddlewareTests
{
    [Fact]
    public async Task Login_has_its_own_threshold_and_slow_warning_contains_safe_stage_timings()
    {
        var logger = new RecordingLogger();
        var options = Options.Create(new RequestPerformanceOptions
        {
            SlowRequestThresholdMilliseconds = 100,
            LoginSlowRequestThresholdMilliseconds = 2_000
        });
        var middleware = new RequestPerformanceMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.Delay(150);
            }, logger, options);

        using var activity = new Activity("login-performance-test").Start();
        activity.SetTag("login.stage.lookup_ms", 123.4);
        var loginContext = new DefaultHttpContext();
        loginContext.Request.Method = HttpMethods.Post;
        loginContext.Request.Path = "/api/v1/auth/login";
        await middleware.InvokeAsync(loginContext);
        Assert.Empty(logger.Warnings);

        var otherContext = new DefaultHttpContext();
        otherContext.Request.Method = HttpMethods.Get;
        otherContext.Request.Path = "/api/v1/centers";
        await middleware.InvokeAsync(otherContext);
        Assert.Single(logger.Warnings);

        var strictLoginMiddleware = new RequestPerformanceMiddleware(
            async context =>
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                await Task.Delay(150);
            }, logger, Options.Create(new RequestPerformanceOptions
            {
                SlowRequestThresholdMilliseconds = 100,
                LoginSlowRequestThresholdMilliseconds = 100
            }));
        await strictLoginMiddleware.InvokeAsync(loginContext);
        Assert.Equal(2, logger.Warnings.Count);
        Assert.Contains("lookup_ms=123.4", logger.Warnings[1], StringComparison.Ordinal);
        Assert.Contains("threshold 100 ms", logger.Warnings[1], StringComparison.Ordinal);
    }

    private sealed class RecordingLogger : ILogger<RequestPerformanceMiddleware>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
        }

        private sealed class EmptyScope : IDisposable
        {
            public static readonly EmptyScope Instance = new();
            public void Dispose() { }
        }
    }
}
