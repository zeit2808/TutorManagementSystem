namespace TutorManagement.Modules.Organizations.Contracts.Rooms;

public sealed record CreateRoomRequest(string Code, string Name, string? LocationNote);

public sealed record UpdateRoomRequest(string Code, string Name, string? LocationNote, int? ExpectedVersion);

public sealed record RoomVersionRequest(int? ExpectedVersion);
