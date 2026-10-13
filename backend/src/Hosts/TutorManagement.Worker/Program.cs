using TutorManagement.Infrastructure;
using TutorManagement.Modules.Academics.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

var connectionString = builder.Configuration.GetConnectionString("MainDatabase")
    ?? throw new InvalidOperationException("Connection string 'MainDatabase' is not configured.");

builder.Services.AddSharedInfrastructure();
builder.Services.AddIdentityAccessInfrastructure(connectionString);
builder.Services.AddOrganizationsInfrastructure(connectionString);
builder.Services.AddAcademicsInfrastructure(connectionString);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
