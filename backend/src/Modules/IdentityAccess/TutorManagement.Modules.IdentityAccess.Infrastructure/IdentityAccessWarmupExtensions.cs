using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure;

public static class IdentityAccessWarmupExtensions
{
    private static readonly TimeSpan WarmupTimeout = TimeSpan.FromSeconds(5);

    public static async Task WarmUpIdentityAccessAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        await using var scope = serviceProvider.CreateAsyncScope();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("IdentityAccessWarmup");
        var stopwatch = Stopwatch.StartNew();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(WarmupTimeout);

        try
        {
            var passwordService = scope.ServiceProvider.GetRequiredService<IPasswordService>();
            passwordService.PerformDummyVerification("Identity-access-startup-warmup");

            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            _ = await userRepository.FindByNormalizedUsernameAsync(
                "__IDENTITY_ACCESS_STARTUP_WARMUP__",
                timeoutSource.Token);
            // Precompile the other read queries used by the first real login.
            // Use impossible identifiers: warm-up must never create a session or change account state.
            _ = await userRepository.FindByIdAsync(Guid.Empty, timeoutSource.Token);
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            _ = await dbContext.LoginSecurityStates.SingleOrDefaultAsync(
                state => state.UserId == Guid.Empty, timeoutSource.Token);

            WarmupLog.Completed(logger, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            WarmupLog.TimedOut(logger, (long)WarmupTimeout.TotalMilliseconds);
        }
        catch (Exception exception)
        {
            WarmupLog.Failed(logger, exception);
        }
    }
}

internal static partial class WarmupLog
{
    [LoggerMessage(
        EventId = 2100,
        Level = LogLevel.Information,
        Message = "Identity Access warm-up completed in {ElapsedMilliseconds} ms.")]
    internal static partial void Completed(ILogger logger, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "Identity Access warm-up exceeded the {TimeoutMilliseconds} ms timeout. The first authentication request may be slower.")]
    internal static partial void TimedOut(ILogger logger, long timeoutMilliseconds);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Warning,
        Message = "Identity Access warm-up failed. The application will continue and readiness checks will report database availability.")]
    internal static partial void Failed(ILogger logger, Exception exception);
}
