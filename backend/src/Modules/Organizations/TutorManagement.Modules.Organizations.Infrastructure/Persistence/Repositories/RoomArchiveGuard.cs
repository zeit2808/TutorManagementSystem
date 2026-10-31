using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

public sealed class RoomArchiveGuard(OrganizationDbContext db) : IRoomArchiveGuard
{
    public async Task<IRoomArchiveTransaction> BeginAsync(Guid centerId, Guid roomId, CancellationToken token)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        try
        {
            var key = $"room:{centerId}:{roomId}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", token);
            return new Guard(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    public Task<bool> HasFutureSessionsAsync(Guid centerId, Guid roomId, DateTimeOffset now, CancellationToken token) =>
        db.Database.SqlQueryRaw<bool>(
            "SELECT EXISTS(SELECT 1 FROM academics.teaching_sessions WHERE center_id = @center AND room_id = @room AND cancelled_at_utc IS NULL AND end_utc > @now) AS \"Value\"",
            new NpgsqlParameter("center", centerId), new NpgsqlParameter("room", roomId),
            new NpgsqlParameter("now", now)).SingleAsync(token);

    private sealed class Guard(IDbContextTransaction transaction) : IRoomArchiveTransaction
    {
        public Task CommitAsync(CancellationToken token) => transaction.CommitAsync(token);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
