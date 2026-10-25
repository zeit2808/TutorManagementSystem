using Microsoft.Extensions.DependencyInjection;

namespace TutorManagement.Modules.Finance.Api;

public static class DependencyInjection
{
    public static IMvcBuilder AddFinanceApi(this IMvcBuilder builder) =>
        builder.AddApplicationPart(typeof(DependencyInjection).Assembly);
}
