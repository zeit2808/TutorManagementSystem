using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.Api.Composition;

/// <summary>
/// Pays EF Core's one-off costs at startup instead of on the first visitor's request: building each module's
/// model and compiling the queries every authenticated request runs (session check, center access) plus the
/// tuition ledger, which alone took ~2 s on the first call after a restart. Uses ids that match nothing, so it
/// reads but never changes data. Failures are logged and ignored: warm-up is an optimisation, not a gate.
/// </summary>
internal static partial class DatabaseWarmup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task WarmUpDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseWarmup");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var token = timeout.Token;
        var stopwatch = Stopwatch.StartNew();
        var nobody = Guid.Empty;

        try
        {
            DbContext[] contexts =
            [
                provider.GetRequiredService<IdentityDbContext>(),
                provider.GetRequiredService<OrganizationDbContext>(),
                provider.GetRequiredService<AcademicsDbContext>(),
                provider.GetRequiredService<FinanceDbContext>()
            ];
            foreach (var context in contexts)
                _ = context.Model;

            _ = await provider.GetRequiredService<IAuthSessionRepository>().IsActiveAsync(nobody, nobody, token);
            _ = await provider.GetRequiredService<ICenterAccessService>().FindAsync(nobody, nobody, token);

            // A center without classes skips the per-class ledger queries, so warm those directly.
            var ledgerReader = provider.GetRequiredService<ITuitionAcademicsReader>();
            Guid[] noClasses = [nobody];
            _ = await ledgerReader.ListSessionsAsync(nobody, noClasses, token);
            _ = await ledgerReader.ListMissedSessionsAsync(nobody, noClasses, token);
            _ = await ledgerReader.ListAbsencesAsync(nobody, noClasses, token);

            var tuition = provider.GetRequiredService<TuitionWorkflow>();
            _ = await tuition.SummaryAsync(nobody, token);
            _ = await tuition.ListChargesAsync(nobody, null, null, null, null, 1, 25, token);

            Log.Done(logger, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or OperationCanceledException)
        {
            Log.Skipped(logger, stopwatch.ElapsedMilliseconds, exception.Message);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
            Message = "Database warm-up finished in {ElapsedMilliseconds} ms.")]
        public static partial void Done(ILogger logger, long elapsedMilliseconds);

        [LoggerMessage(EventId = 3102, Level = LogLevel.Warning,
            Message = "Database warm-up stopped after {ElapsedMilliseconds} ms: {Reason}")]
        public static partial void Skipped(ILogger logger, long elapsedMilliseconds, string reason);
    }
}
