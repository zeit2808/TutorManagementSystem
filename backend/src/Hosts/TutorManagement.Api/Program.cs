using Asp.Versioning;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TutorManagement.Api.HealthChecks;
using TutorManagement.Api.Middleware;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.Academics.Api;
using TutorManagement.Modules.Academics.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Api;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.Organizations.Api;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.WebApi.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var connectionString = builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is not configured.");

builder.Services.AddWebApiFoundation();
builder.Services.AddSharedInfrastructure();
builder.Services.AddHttpLogging(_ => { });
builder.Services.AddOpenApi();
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

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
    .AddControllers(options => options.ReturnHttpNotAcceptable = true)
    .AddXmlSerializerFormatters();

mvcBuilder
    .AddIdentityAccessApi()
    .AddOrganizationsApi()
    .AddAcademicsApi();

builder.Services.AddIdentityAccessInfrastructure(connectionString);
builder.Services.AddOrganizationsInfrastructure(connectionString);
builder.Services.AddAcademicsInfrastructure(connectionString);

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
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

app.UseWebApiFoundation();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseHttpLogging();
app.UseCors("Frontend");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
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

public partial class Program;
