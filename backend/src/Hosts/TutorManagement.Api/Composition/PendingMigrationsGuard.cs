using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.Api.Composition;

/// <summary>
/// The API never migrates the database itself (DbMigrator does). When the code is newer than the database,
/// every query that touches a new column fails at request time with a bare "column ... does not exist".
/// This check runs once at startup and stops the API with a message naming the missing migrations instead.
/// </summary>
internal static partial class PendingMigrationsGuard
{
    public static async Task EnsureDatabaseIsCurrentAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PendingMigrationsGuard");
        var contexts = new DbContext[]
        {
            scope.ServiceProvider.GetRequiredService<OrganizationDbContext>(),
            scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
            scope.ServiceProvider.GetRequiredService<AcademicsDbContext>(),
            scope.ServiceProvider.GetRequiredService<FinanceDbContext>()
        };

        var pending = new List<string>();
        foreach (var context in contexts)
        {
            try
            {
                foreach (var migration in await context.Database.GetPendingMigrationsAsync(cancellationToken))
                    pending.Add($"{context.GetType().Name}: {migration}");
            }
            catch (DbException exception)
            {
                // Database unreachable: /health/ready reports it; do not block startup for that here.
                Log.CheckSkipped(logger, context.GetType().Name, exception.Message);
                return;
            }
        }

        if (pending.Count == 0)
            return;

        throw new InvalidOperationException(
            "Database chưa được cập nhật theo code hiện tại (còn " + pending.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            " migration chưa chạy). Hãy tắt API rồi chạy: dotnet run --project src/Hosts/TutorManagement.DbMigrator" +
            Environment.NewLine + string.Join(Environment.NewLine, pending));
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
            Message = "Skipped the pending-migration check for {Context}: {Reason}")]
        public static partial void CheckSkipped(ILogger logger, string context, string reason);
    }
}
