using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Email;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.IntegrationTests;

public sealed class EmailWorkerRegistrationTests
{
    [Fact]
    public void Email_worker_resolves_processor_without_organizations_or_academics()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedInfrastructure();
        services.AddIdentityAccessEmailWorker(
            "Host=localhost;Database=tutor_management;Username=tutor_app",
            new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EmailOutboxProcessor>());
        Assert.Null(scope.ServiceProvider.GetService<IOrganizationCenterReader>());
    }
}
