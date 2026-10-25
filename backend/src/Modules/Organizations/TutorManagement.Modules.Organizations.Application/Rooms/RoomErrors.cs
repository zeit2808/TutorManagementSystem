using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Rooms;

public static class RoomErrors
{
    public static readonly Error NotFound = new("room_not_found", "The room was not found in this center.");
    public static readonly Error CenterNotReady = new("room_center_not_approved", "Only an approved center can manage rooms.");
    public static readonly Error InvalidDetails = new("invalid_room_details", "The room details are invalid.");
    public static readonly Error CodeAlreadyExists = new("room_code_already_exists", "A room with this code already exists in this center.", "code");
    public static readonly Error NameAlreadyExists = new("room_name_already_exists", "A room with this name already exists in this center.", "name");
    public static readonly Error AlreadyArchived = new("room_already_archived", "The room is already archived.");
    public static readonly Error AlreadyActive = new("room_already_active", "The room is already active.");
    public static readonly Error ArchivedCannotEdit = new("room_archived_cannot_edit", "Reactivate the room before editing it.");
    public static readonly Error ConcurrentChange = new("room_concurrent_change", "The room changed since it was loaded. Reload and try again.");
    public static readonly Error HasFutureSessions = new("room_has_future_sessions", "Cancel future sessions before archiving this room.");
}
