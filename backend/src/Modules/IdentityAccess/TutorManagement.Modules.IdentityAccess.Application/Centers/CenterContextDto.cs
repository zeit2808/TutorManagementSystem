namespace TutorManagement.Modules.IdentityAccess.Application.Centers;

public sealed class CenterContextDto
{
    public Guid CenterId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public bool CanAccess { get; set; }
}
