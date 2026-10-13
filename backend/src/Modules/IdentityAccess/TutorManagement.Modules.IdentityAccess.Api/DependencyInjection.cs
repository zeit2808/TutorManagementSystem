using Microsoft.Extensions.DependencyInjection;

namespace TutorManagement.Modules.IdentityAccess.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddIdentityAccessApi(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
