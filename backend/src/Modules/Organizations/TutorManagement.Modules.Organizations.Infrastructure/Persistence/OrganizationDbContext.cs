using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.SharedKernel.Exceptions;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence;

public sealed class OrganizationDbContext(DbContextOptions<OrganizationDbContext> options)
    : DbContext(options), IOrganizationUnitOfWork
{
    public const string Schema = "organization";

    public DbSet<Center> Centers => Set<Center>();
    public DbSet<CenterChangeRequest> CenterChangeRequests => Set<CenterChangeRequest>();
    public DbSet<Room> Rooms => Set<Room>();

    async Task<int> IOrganizationUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException)
        {
            throw new OrganizationDuplicateException(
                postgresException.ConstraintName ?? string.Empty,
                exception);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The center was changed by another request. Reload and try again.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrganizationDbContext).Assembly);
    }
}
