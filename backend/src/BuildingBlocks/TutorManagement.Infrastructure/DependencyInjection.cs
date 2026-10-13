using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Infrastructure.Clock;

namespace TutorManagement.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSharedInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}
