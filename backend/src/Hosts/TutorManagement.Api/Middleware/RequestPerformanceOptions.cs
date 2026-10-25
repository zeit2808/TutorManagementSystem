namespace TutorManagement.Api.Middleware;

public sealed class RequestPerformanceOptions
{
    public const string SectionName = "RequestPerformance";

    public int SlowRequestThresholdMilliseconds { get; set; } = 1_000;

    // Password verification is intentionally CPU-expensive and login also persists a session and audit event.
    public int LoginSlowRequestThresholdMilliseconds { get; set; } = 2_000;

    public bool IsValid() =>
        SlowRequestThresholdMilliseconds is >= 100 and <= 60_000 &&
        LoginSlowRequestThresholdMilliseconds is >= 100 and <= 60_000;
}
