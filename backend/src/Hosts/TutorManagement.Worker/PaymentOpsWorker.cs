using TutorManagement.Modules.Finance.Application.Payments;

namespace TutorManagement.Worker;

/// <summary>
/// Đợt 5 (docs/THANH-TOAN-QR-KE-HOACH.md): the nightly reconciliation, the minute D10 monitor and the 07:00 digest.
/// Its own BackgroundService so a slow 3-day scan never delays pulling or booking. Work is leased or claimed in the
/// database, so several Workers can run.
/// </summary>
public sealed partial class PaymentOpsWorker(IServiceScopeFactory scopeFactory, ILogger<PaymentOpsWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MonitorEvery = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastMonitor = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                    await scope.ServiceProvider.GetRequiredService<PaymentReconciler>().RunOnceAsync(2, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { LogReconcileFailed(logger, exception); }

            if (DateTimeOffset.UtcNow - lastMonitor >= MonitorEvery)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<PaymentMonitor>().RunOnceAsync(stoppingToken);
                    lastMonitor = DateTimeOffset.UtcNow;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception) { LogMonitorFailed(logger, exception); }
            }

            try { await Task.Delay(Tick, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Error, Message = "Payment reconciliation failed; it is retried after its lease.")]
    private static partial void LogReconcileFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Payment monitor failed; it runs again next minute.")]
    private static partial void LogMonitorFailed(ILogger logger, Exception exception);
}
