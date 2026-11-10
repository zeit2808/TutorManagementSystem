using TutorManagement.Modules.Finance.Application.Payments;

namespace TutorManagement.Worker;

/// <summary>
/// Pulls bank transactions (docs/THANH-TOAN-QR-KE-HOACH.md, Round 2e). Its own BackgroundService so a slow bank feed
/// never delays email delivery or billing, and the other way round. Polls every second; work is leased in the
/// database, so several Workers can run.
/// </summary>
public sealed partial class PaymentWorker(IServiceScopeFactory scopeFactory, ILogger<PaymentWorker> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var pulled = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                pulled = await scope.ServiceProvider.GetRequiredService<PaymentFetcher>().RunOnceAsync(BatchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogPullFailed(logger, exception);
            }

            if (pulled >= BatchSize) continue;
            try { await Task.Delay(Idle, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error, Message = "Bank feed pull failed; it is retried.")]
    private static partial void LogPullFailed(ILogger logger, Exception exception);
}
