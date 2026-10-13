using Microsoft.EntityFrameworkCore;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence;

public sealed class AcademicsDbContext(DbContextOptions<AcademicsDbContext> options) : DbContext(options)
{
    public const string Schema = "academics";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AcademicsDbContext).Assembly);
    }
}
