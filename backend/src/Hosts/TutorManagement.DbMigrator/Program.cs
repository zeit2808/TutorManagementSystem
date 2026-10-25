using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.Academics.Infrastructure;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Seeding;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var connectionString = builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is not configured.");

builder.Services.AddSharedInfrastructure();
builder.Services.AddIdentityAccessInfrastructure(connectionString);
builder.Services.AddOrganizationsInfrastructure(connectionString);
builder.Services.AddAcademicsInfrastructure(connectionString);
builder.Services.AddFinanceReminders(connectionString);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();
var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrator");

await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(
        "CREATE SCHEMA IF NOT EXISTS iam; " +
        "CREATE SCHEMA IF NOT EXISTS organization; " +
        "CREATE SCHEMA IF NOT EXISTS academics; " +
        "CREATE SCHEMA IF NOT EXISTS finance;",
        connection);
    await command.ExecuteNonQueryAsync();
}

var contexts = new DbContext[]
{
    scope.ServiceProvider.GetRequiredService<OrganizationDbContext>(),
    scope.ServiceProvider.GetRequiredService<IdentityDbContext>(),
    scope.ServiceProvider.GetRequiredService<AcademicsDbContext>(),
    scope.ServiceProvider.GetRequiredService<FinanceDbContext>()
};

foreach (var context in contexts)
{
    MigrationLog.Applying(logger, context.GetType().Name);
    await context.Database.MigrateAsync();
}

if (builder.Environment.IsDevelopment())
{
    var seedPassword = builder.Configuration["Seed:OwnerPassword"];
    if (!string.IsNullOrWhiteSpace(seedPassword))
    {
        var seedUsername = builder.Configuration["Seed:OwnerUsername"] ?? "admin";
        var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedOwnerAsync(seedUsername, seedPassword, CancellationToken.None);
        MigrationLog.OwnerSeeded(logger, seedUsername);
    }
}

MigrationLog.Completed(logger);

internal static partial class MigrationLog
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Applying migrations for {DbContext}.")]
    internal static partial void Applying(ILogger logger, string dbContext);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "All database migrations were applied successfully.")]
    internal static partial void Completed(ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Development owner account '{Username}' is available.")]
    internal static partial void OwnerSeeded(ILogger logger, string username);
}
