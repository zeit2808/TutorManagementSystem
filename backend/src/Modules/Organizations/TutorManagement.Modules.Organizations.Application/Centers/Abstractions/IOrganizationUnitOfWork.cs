namespace TutorManagement.Modules.Organizations.Application.Centers.Abstractions;

public interface IOrganizationUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
