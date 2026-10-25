namespace TutorManagement.Modules.IdentityAccess.Contracts.Authorization;

public static class CenterAccessPolicyNames
{
    public const string Member = "CenterMember";
    public const string Management = "CenterManagement";
    public const string Owner = "CenterOwner";

    /// <summary>
    /// HttpContext.Items key holding the caller's center role (e.g. "Teacher") once a center policy
    /// has succeeded, so endpoints open to every member can narrow what a non-manager may do.
    /// </summary>
    public const string RoleItemKey = "tm.center-role";

    public static bool IsManagementRole(string? role) => role is "CenterOwner" or "CenterAdmin";
}
