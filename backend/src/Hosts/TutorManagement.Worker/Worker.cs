namespace TutorManagement.Worker;

public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            LogHeartbeat(logger, DateTimeOffset.UtcNow);
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Tutor Management worker started.")]
    private static partial void LogStarted(ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Worker heartbeat at {OccurredAtUtc}.")]
    private static partial void LogHeartbeat(ILogger logger, DateTimeOffset occurredAtUtc);
}
