namespace TutorManagement.Application.Abstractions.Transactions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
