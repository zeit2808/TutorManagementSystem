using Microsoft.Extensions.DependencyInjection;

namespace TutorManagement.Modules.Academics.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddAcademicsApi(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
