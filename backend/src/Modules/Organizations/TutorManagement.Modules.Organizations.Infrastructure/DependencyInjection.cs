using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.Modules.Organizations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrganizationsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<OrganizationDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", OrganizationDbContext.Schema)));

        return services;
    }
}
