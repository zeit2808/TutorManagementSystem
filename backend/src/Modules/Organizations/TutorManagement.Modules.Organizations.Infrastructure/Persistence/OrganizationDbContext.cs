using Microsoft.EntityFrameworkCore;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options) : DbContext(options)
{
    public const string Schema = "organization";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
