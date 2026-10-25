using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Repositories;

public sealed class RoomRepository(OrganizationDbContext dbContext) : IRoomRepository
{
    public Task<Room?> FindAsync(Guid centerId, Guid roomId, CancellationToken cancellationToken) =>
        dbContext.Rooms.SingleOrDefaultAsync(room => room.CenterId == centerId && room.Id == roomId, cancellationToken);

    public async Task<IReadOnlyList<Room>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
        await dbContext.Rooms.AsNoTracking()
            .Where(room => room.CenterId == centerId && (includeArchived || room.ArchivedAtUtc == null))
            .OrderBy(room => room.NormalizedName).ThenBy(room => room.NormalizedCode)
            .ToArrayAsync(cancellationToken);

    public Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId,
        CancellationToken cancellationToken) =>
        dbContext.Rooms.AnyAsync(room => room.CenterId == centerId && room.NormalizedCode == normalizedCode &&
            (!excludingId.HasValue || room.Id != excludingId.Value), cancellationToken);

    public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
        CancellationToken cancellationToken) =>
        dbContext.Rooms.AnyAsync(room => room.CenterId == centerId && room.NormalizedName == normalizedName &&
            (!excludingId.HasValue || room.Id != excludingId.Value), cancellationToken);

    public async Task AddAsync(Room room, CancellationToken cancellationToken) =>
        await dbContext.Rooms.AddAsync(room, cancellationToken);
}
