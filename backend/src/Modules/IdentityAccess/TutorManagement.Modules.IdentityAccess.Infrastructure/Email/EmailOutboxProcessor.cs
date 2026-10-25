using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Email;

public sealed partial class EmailOutboxProcessor(
    IdentityDbContext dbContext,
    IEmailDeliveryClient deliveryClient,
    IOptions<EmailDeliveryOptions> options,
    IClock clock,
    ILogger<EmailOutboxProcessor> logger)
{
    private readonly EmailDeliveryOptions _options = options.Value;

    public async Task<EmailOutboxBatchResult> ProcessPendingAsync(
        CancellationToken cancellationToken)
    {
        // Claim and commit before calling the external delivery provider. SKIP LOCKED
        // prevents two workers from claiming the same row without holding a database
        // transaction open during SMTP I/O.
        var now = clock.UtcNow;
        var leaseDuration = TimeSpan.FromMilliseconds(
            (long)Math.Ceiling((double)_options.BatchSize / _options.MaximumConcurrency) *
            Math.Max(_options.Smtp.TimeoutMilliseconds, 30_000) + 60_000);
        List<EmailOutboxMessage> messages;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            messages = await dbContext.EmailOutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM iam.email_outbox_messages
                WHERE processed_at_utc IS NULL
                  AND (attempt_count < {_options.MaximumAttempts} OR security_token_id IS NOT NULL)
                  AND (processing_until_utc IS NULL OR processing_until_utc <= {now})
                ORDER BY created_at_utc
                LIMIT {_options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);

            if (messages.Count > 0)
            {
                var processingId = Guid.NewGuid();
                foreach (var message in messages)
                    message.Claim(processingId, now.Add(leaseDuration));
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }

        if (messages.Count == 0)
        {
            return new EmailOutboxBatchResult(0, 0, 0);
        }

        // OTP emails are useful only while their linked token is active. A resend
        // revokes the previous token, so the worker must not deliver stale codes.
        var securityTokenIds = messages.Where(message => message.SecurityTokenId.HasValue)
            .Select(message => message.SecurityTokenId!.Value).ToArray();
        var linkedTokens = await dbContext.UserSecurityTokens.AsNoTracking()
            .Where(token => securityTokenIds.Contains(token.Id))
            .ToDictionaryAsync(token => token.Id, cancellationToken);
        var deliverable = messages.Select(message =>
            message.AttemptCount < _options.MaximumAttempts &&
            (message.SecurityTokenId is not Guid tokenId ||
             (linkedTokens.TryGetValue(tokenId, out var token) && token.IsActive(clock.UtcNow)))).ToArray();

        var attempts = new DeliveryAttempt[messages.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, messages.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.MaximumConcurrency,
                CancellationToken = cancellationToken
            },
            async (index, token) =>
            {
                var message = messages[index];
                if (!deliverable[index])
                {
                    attempts[index] = DeliveryAttempt.Skipped;
                    return;
                }
                try
                {
                    await deliveryClient.DeliverAsync(
                        new EmailDeliveryRequest(
                            message.Id,
                            message.Recipient,
                            message.Subject,
                            message.Body,
                            message.CreatedAtUtc),
                        token);
                    attempts[index] = DeliveryAttempt.Success;
                }
                catch (PermanentEmailDeliveryException exception)
                {
                    attempts[index] = DeliveryAttempt.Failed(exception, permanent: true);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    attempts[index] = DeliveryAttempt.Failed(exception, permanent: false);
                }
            });

        var processedCount = 0;
        var failedCount = 0;
        for (var index = 0; index < messages.Count; index++)
        {
            var message = messages[index];
            var attempt = attempts[index];
            if (attempt.Outcome == DeliveryOutcome.Sent)
            {
                message.MarkProcessed(clock.UtcNow);
                processedCount++;
                continue;
            }

            if (attempt.Outcome == DeliveryOutcome.Skipped)
            {
                message.MarkSkipped(message.AttemptCount >= _options.MaximumAttempts
                    ? "Không gửi: đã hết số lần thử."
                    : "Không gửi: mã trong thư đã hết hạn hoặc đã có mã mới.", clock.UtcNow);
                processedCount++;
                continue;
            }

            var exception = attempt.Exception
                ?? new InvalidOperationException("Email delivery failed without an exception.");
            if (attempt.Outcome == DeliveryOutcome.Rejected)
                message.MarkUndeliverable(exception.Message, _options.MaximumAttempts);
            else
                message.MarkFailed(exception.Message, clock.UtcNow.Add(RetryDelay(message.AttemptCount + 1)));
            failedCount++;
            LogDeliveryFailed(logger, message.Id, message.Recipient, exception);
        }

        // An E2E cleanup (or an expired lease taken by another worker) can remove
        // ownership after delivery. Do not let one lost row block the rest of the batch.
        while (true)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException exception) when (exception.Entries.Count > 0)
            {
                foreach (var entry in exception.Entries)
                {
                    if (entry.Entity is not EmailOutboxMessage message)
                        throw;

                    if (message.ProcessedAtUtc.HasValue)
                        processedCount--;
                    else
                        failedCount--;

                    LogClaimLost(logger, message.Id);
                    entry.State = EntityState.Detached;
                }
            }
        }

        return new EmailOutboxBatchResult(messages.Count, processedCount, failedCount);
    }

    /// <summary>Wait after the n-th failed attempt: 1 min, 5 min, 15 min, then hourly.</summary>
    public static TimeSpan RetryDelay(int failedAttempts) => failedAttempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromHours(1)
    };

    private enum DeliveryOutcome { Sent, Skipped, Failed, Rejected }

    private sealed record DeliveryAttempt(DeliveryOutcome Outcome, Exception? Exception)
    {
        public static readonly DeliveryAttempt Success = new(DeliveryOutcome.Sent, null);

        public static readonly DeliveryAttempt Skipped = new(DeliveryOutcome.Skipped, null);

        public static DeliveryAttempt Failed(Exception exception, bool permanent) =>
            new(permanent ? DeliveryOutcome.Rejected : DeliveryOutcome.Failed, exception);
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Error,
        Message = "Could not deliver email outbox message {MessageId} to {Recipient}.")]
    private static partial void LogDeliveryFailed(
        ILogger logger,
        Guid messageId,
        string recipient,
        Exception exception);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Warning,
        Message = "Email outbox message {MessageId} was deleted or its processing lease was lost before the delivery result could be saved.")]
    private static partial void LogClaimLost(ILogger logger, Guid messageId);
}
