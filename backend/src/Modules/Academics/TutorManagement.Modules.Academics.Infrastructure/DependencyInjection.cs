using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;

namespace TutorManagement.Modules.Academics.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicsInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AcademicsDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", AcademicsDbContext.Schema)));

        return services;
    }
}
