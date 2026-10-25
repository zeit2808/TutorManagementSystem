namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

public interface IIdentityUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    Task<IIdentityTransaction> BeginLoginTransactionAsync(CancellationToken cancellationToken = default) =>
        BeginTransactionAsync(cancellationToken);

    /// <summary>Forgets every pending change and loaded entity so the next read sees the database as it is now.</summary>
    void DiscardChanges()
    {
    }
}

public interface IIdentityTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
