using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Serialization;
using TutorManagement.WebApi.Exceptions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.WebApi.Extensions;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApiFoundation(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        // Apply the same JSON contract to MVC and middleware WriteAsJsonAsync responses.
        // Keep false, 0 and empty arrays: they carry business meaning.
        services.PostConfigure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        services.PostConfigure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
            options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
        // PostConfigure keeps the application's response envelope authoritative even
        // when API versioning/MVC packages register their own option configuration.
        services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value is not null)
                    .SelectMany(entry => entry.Value!.Errors.Select(error =>
                        new ApiError(
                            "validation_error",
                            string.IsNullOrWhiteSpace(error.ErrorMessage)
                                ? "The request value is invalid."
                                : error.ErrorMessage,
                            entry.Key)))
                    .ToArray();

                return new BadRequestObjectResult(
                    ApiResponse<object>.Failure(
                        errors,
                        context.HttpContext.TraceIdentifier,
                        "Request validation failed."));
            };
        });
        return services;
    }

    public static IApplicationBuilder UseWebApiFoundation(this IApplicationBuilder app)
    {
        app.UseExceptionHandler();
        return app;
    }
}
