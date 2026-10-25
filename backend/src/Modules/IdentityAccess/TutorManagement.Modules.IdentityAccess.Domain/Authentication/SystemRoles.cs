namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public static class SystemRoles
{
    public static readonly Guid PlatformAdminId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static readonly Guid ManagerId = Guid.Parse("10000000-0000-0000-0000-000000000002");

    public static readonly Guid TeacherId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    public static readonly Guid StudentId = Guid.Parse("10000000-0000-0000-0000-000000000004");

    public const string PlatformAdmin = "PlatformAdmin";

    public const string Manager = "Manager";

    public const string Teacher = "Teacher";

    public const string Student = "Student";
}
