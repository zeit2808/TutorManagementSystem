using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.WebApi.Exceptions;

namespace TutorManagement.WebApi.Extensions;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiFoundation(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }

    public static IApplicationBuilder UseWebApiFoundation(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        return app;
    }
}
