using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Payroll;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Finance.Infrastructure.Security;

namespace TutorManagement.Modules.Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddFinanceReminders(connectionString);
        services.AddScoped<TuitionWorkflow>();
        services.AddScoped<PaymentIntentWorkflow>();
        services.AddScoped<StudentTuitionQuery>();
        services.AddScoped<IPayrollStore, PayrollStore>();
        services.AddScoped<IPayrollAcademicsReader, PayrollAcademicsReader>();
        services.AddScoped<PayrollWorkflow>();
        return services;
    }

    /// <summary>What the Worker needs for the daily billing and reminder run (no user directory).</summary>
    public static IServiceCollection AddFinanceReminders(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FinanceDbContext>(options =>
            options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", FinanceDbContext.Schema)));
        services.AddScoped<ITuitionStore, TuitionStore>();
        services.AddScoped<ITuitionAcademicsReader, TuitionAcademicsReader>();
        services.AddScoped<TuitionReminders>();
        // Built on first use: hosts that never read payment secrets need no keys configured.
        services.AddSingleton<ISecretProtector>(provider =>
            new AesGcmSecretProtector(PaymentSecretOptions.From(provider.GetRequiredService<IConfiguration>())));
        return services;
    }
}
