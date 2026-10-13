using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityAccessInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.Schema)));

        return services;
    }
}
