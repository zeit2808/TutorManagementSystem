using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Payroll;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Payments;
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
        services.AddScoped<PaymentProviderWorkflow>();
        services.AddScoped<FinanceSettingsWorkflow>();
        services.AddScoped<PaymentQueueWorkflow>();
        services.AddScoped<PaymentPullStore>();
        services.AddScoped<PaymentOpsStore>();
        services.AddScoped<IPaymentOpsStore>(provider => provider.GetRequiredService<PaymentOpsStore>());
        services.AddScoped<IPaymentOpsReader>(provider => provider.GetRequiredService<PaymentOpsStore>());
        services.AddScoped<PaymentOpsWorkflow>();
        services.AddScoped<IPaymentWebhookStore, PaymentWebhookStore>();
        services.AddScoped<SepayWebhookReceiver>();
        services.AddSingleton(provider =>
        {
            var configured = provider.GetRequiredService<IConfiguration>()["Payments:WebhookTimestampToleranceSeconds"];
            var seconds = int.TryParse(configured, out var value) ? value : 300;
            if (seconds is < 60 or > 6 * 60 * 60)
                throw new InvalidOperationException("Payments:WebhookTimestampToleranceSeconds must be 60–21600.");
            return new WebhookOptions { TimestampToleranceSeconds = seconds };
        });
        services.AddScoped<StudentTuitionQuery>();
        services.AddScoped<IPayrollStore, PayrollStore>();
        services.AddScoped<IPayrollAcademicsReader, PayrollAcademicsReader>();
        services.AddScoped<PayrollWorkflow>();
        return services;
    }

    /// <summary>
    /// The Worker's bank-feed pull (Round 2e). One shared HttpClient with a bounded connection lifetime;
    /// <c>Sepay:ApiBaseUrl</c> chooses production or the sandbox.
    /// </summary>
    public static IServiceCollection AddFinancePaymentPulling(this IServiceCollection services)
    {
        services.AddSingleton(provider => SepayOptions.From(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<IBankFeedProvider>(provider => new SepayBankFeed(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(20)
            },
            provider.GetRequiredService<SepayOptions>()));
        services.AddScoped<PaymentPullStore>();
        services.AddScoped<IPaymentPullStore>(provider => provider.GetRequiredService<PaymentPullStore>());
        services.AddScoped<PaymentFetcher>();
        services.AddScoped<IPaymentBookingStore, PaymentBookingStore>();
        services.AddScoped<PaymentBooker>();
        // Đợt 5: nightly reconciliation, D10 alerts and the morning digest.
        services.AddScoped<PaymentOpsStore>();
        services.AddScoped<IPaymentOpsStore>(provider => provider.GetRequiredService<PaymentOpsStore>());
        services.AddScoped<PaymentAlertBook>();
        services.AddScoped<PaymentReconciler>();
        services.AddScoped<PaymentMonitor>();
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
