using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.Modules.Organizations.Application.Rooms;

public sealed class RoomDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LocationNote { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }

    public static RoomDto From(Room room) => new()
    {
        Id = room.Id,
        Code = room.Code,
        Name = room.Name,
        LocationNote = room.LocationNote,
        IsActive = room.IsActive,
        Version = room.Version
    };
}
