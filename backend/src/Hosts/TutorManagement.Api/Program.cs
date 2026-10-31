using Asp.Versioning;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerUI;
using TutorManagement.Api.Authentication;
using TutorManagement.Api.Composition;
using TutorManagement.Api.Development;
using TutorManagement.Api.HealthChecks;
using TutorManagement.Api.Middleware;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.Academics.Api;
using TutorManagement.Modules.Academics.Infrastructure;
using TutorManagement.Modules.Finance.Api;
using TutorManagement.Modules.Finance.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Api;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.Organizations.Api;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var connectionString = builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is not configured.");

var trustedProxies = builder.Configuration.GetSection("RateLimit:TrustedProxies").Get<string[]>() ?? [];
var loginPermitLimit = builder.Configuration.GetValue("RateLimit:LoginPermitLimit", 30);
var loginWindowSeconds = builder.Configuration.GetValue("RateLimit:LoginWindowSeconds", 60);
if (loginPermitLimit is < 1 or > 1000 || loginWindowSeconds is < 1 or > 3600)
    throw new InvalidOperationException("Login rate limit configuration is outside the supported range.");
if (trustedProxies.Length > 0)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var value in trustedProxies)
        {
            if (!IPAddress.TryParse(value, out var address))
                throw new InvalidOperationException("RateLimit:TrustedProxies contains an invalid IP address.");
            options.KnownProxies.Add(address);
        }
    });
}

builder.Services.AddWebApiFoundation();
builder.Services.AddOptions<RequestPerformanceOptions>()
    .Bind(builder.Configuration.GetSection(RequestPerformanceOptions.SectionName))
    .Validate(options => options.IsValid(), "Request performance thresholds must be between 100 and 60000 ms.")
    .ValidateOnStart();
builder.Services.AddSharedInfrastructure();
builder.Services.AddHttpLogging(_ => { });
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Tutor Management API",
        Version = "v1",
        Description = "API for the Tutor Management modular monolith."
    });
    options.CustomSchemaIds(CreateSwaggerSchemaId);

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the JWT access token only. Swagger adds the Bearer prefix automatically."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("bearer", document)] = []
    });
});
builder.Services.AddTutorAuthentication(builder.Configuration);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new ValueTask(context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Failure(
                [new ApiError("rate_limit_exceeded", "Too many requests. Please try again later.")],
                context.HttpContext.TraceIdentifier,
                "Rate limit exceeded."), cancellationToken));
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginPermitLimit,
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-recovery", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromHours(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-reset", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("payment-intent", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("password-authenticated", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1.0);
        options.AssumeDefaultVersionWhenUnspecified = false;
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

var mvcBuilder = builder.Services
    .AddControllers(options =>
    {
        options.ReturnHttpNotAcceptable = true;
        options.Filters.Add(new ProducesAttribute("application/json", "application/xml"));
    })
    .AddXmlSerializerFormatters();

mvcBuilder
    .AddIdentityAccessApi()
    .AddOrganizationsApi()
    .AddAcademicsApi()
    .AddFinanceApi();

builder.Services.AddIdentityAccessInfrastructure(connectionString);
builder.Services.AddOrganizationsInfrastructure(connectionString);
builder.Services.AddAcademicsInfrastructure(connectionString);
builder.Services.AddFinanceInfrastructure(connectionString);

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

var app = builder.Build();

if (trustedProxies.Length > 0)
    app.UseForwardedHeaders();

if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.EnsureDatabaseIsCurrentAsync(app.Lifetime.ApplicationStopping);
    await app.Services.WarmUpIdentityAccessAsync(app.Lifetime.ApplicationStopping);
}

app.UseWebApiFoundation();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestPerformanceMiddleware>();
app.UseHttpLogging();
app.UseRouting();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Tutor Management API v1");
        options.DocumentTitle = "Tutor Management API";
        options.DisplayRequestDuration();
        options.EnablePersistAuthorization();
        options.DocExpansion(DocExpansion.None);
        options.DefaultModelsExpandDepth(-1);
    });
}

if (app.Environment.IsDevelopment())
    app.MapGoogleLoginTestPage();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

static string CreateSwaggerSchemaId(Type type)
{
    if (!type.IsGenericType)
    {
        return (type.FullName ?? type.Name)
            .Replace('.', '_')
            .Replace('+', '_');
    }

    var genericTypeName = type.GetGenericTypeDefinition().FullName ?? type.Name;
    genericTypeName = genericTypeName[..genericTypeName.IndexOf('`')]
        .Replace('.', '_')
        .Replace('+', '_');
    var argumentNames = string.Join('_', type.GetGenericArguments().Select(CreateSwaggerSchemaId));
    return $"{genericTypeName}_{argumentNames}";
}

public partial class Program;
