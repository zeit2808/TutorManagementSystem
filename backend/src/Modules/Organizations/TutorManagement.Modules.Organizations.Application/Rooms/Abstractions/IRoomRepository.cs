using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;

public interface IRoomRepository
{
    Task<Room?> FindAsync(Guid centerId, Guid roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Room>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId, CancellationToken cancellationToken);
    Task AddAsync(Room room, CancellationToken cancellationToken);
}
