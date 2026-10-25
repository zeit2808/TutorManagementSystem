namespace TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;

public interface IRoomArchiveGuard
{
    Task<IRoomArchiveTransaction> BeginAsync(Guid centerId, Guid roomId, CancellationToken token);
    Task<bool> HasFutureSessionsAsync(Guid centerId, Guid roomId, DateTimeOffset now, CancellationToken token);
}

public interface IRoomArchiveTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken token);
}
