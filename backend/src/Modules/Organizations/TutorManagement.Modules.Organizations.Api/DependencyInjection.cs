using Microsoft.Extensions.DependencyInjection;

namespace TutorManagement.Modules.Organizations.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddOrganizationsApi(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
