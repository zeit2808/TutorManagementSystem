using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace TutorManagement.Worker;

public sealed partial class Worker(
    IServiceScopeFactory scopeFactory,
    IOptions<EmailDeliveryOptions> options,
    ILogger<Worker> logger) : BackgroundService
{
    private readonly EmailDeliveryOptions _options = options.Value;
    private DateTimeOffset _nextAuditCleanupUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTuitionRunUtc = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, _options.Provider);

        while (!stoppingToken.IsCancellationRequested)
        {
            var shouldDelay = true;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>();
                var result = await processor.ProcessPendingAsync(stoppingToken);
                if (DateTimeOffset.UtcNow >= _nextAuditCleanupUtc)
                {
                    _nextAuditCleanupUtc = DateTimeOffset.UtcNow.AddMinutes(5);
                    try
                    {
                        var retention = scope.ServiceProvider.GetRequiredService<SecurityAuditRetentionProcessor>();
                        var deleted = await retention.DeleteExpiredBatchAsync(stoppingToken);
                        _nextAuditCleanupUtc = DateTimeOffset.UtcNow.Add(
                            deleted >= 1000 ? TimeSpan.FromMinutes(1) : TimeSpan.FromDays(1));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        LogAuditCleanupFailed(logger, exception);
                    }
                }
                if (DateTimeOffset.UtcNow >= _nextTuitionRunUtc)
                {
                    // H15: bill new cycles and remind overdue charges; hourly is enough, each charge is reminded once.
                    _nextTuitionRunUtc = DateTimeOffset.UtcNow.AddHours(1);
                    try
                    {
                        var reminders = scope.ServiceProvider.GetRequiredService<TuitionReminders>();
                        var sent = await reminders.RunAsync(stoppingToken);
                        if (sent > 0) LogTuitionReminders(logger, sent);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        LogTuitionFailed(logger, exception);
                    }
                }
                if (result.SelectedCount > 0)
                {
                    LogEmailsProcessed(
                        logger,
                        result.ProcessedCount,
                        result.FailedCount);
                }

                shouldDelay =
                    result.SelectedCount < _options.BatchSize || result.FailedCount > 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogProcessingFailed(logger, exception);
            }

            if (!shouldDelay)
            {
                continue;
            }

            try
            {
                await Task.Delay(_options.PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Information,
        Message = "Tutor Management worker started with {EmailProvider} email delivery.")]
    private static partial void LogStarted(
        ILogger logger,
        EmailDeliveryProvider emailProvider);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Identity email outbox batch completed. Processed: {ProcessedCount}; Failed: {FailedCount}.")]
    private static partial void LogEmailsProcessed(
        ILogger logger,
        int processedCount,
        int failedCount);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Email outbox processing failed.")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Security audit retention batch failed; email processing continues.")]
    private static partial void LogAuditCleanupFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Information, Message = "Queued {Count} tuition reminder emails.")]
    private static partial void LogTuitionReminders(ILogger logger, int count);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Tuition billing / reminder run failed; email processing continues.")]
    private static partial void LogTuitionFailed(ILogger logger, Exception exception);
}
