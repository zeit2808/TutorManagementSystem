using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;

namespace TutorManagement.Modules.IdentityAccess.Api.Authorization;

public static class CenterAccessPolicies
{
    public const string Member = CenterAccessPolicyNames.Member;
    public const string Management = CenterAccessPolicyNames.Management;
    public const string Owner = CenterAccessPolicyNames.Owner;

    public static readonly CenterMemberRole[] MemberRoles =
    [
        CenterMemberRole.CenterOwner,
        CenterMemberRole.CenterAdmin,
        CenterMemberRole.Teacher,
        CenterMemberRole.Staff
    ];

    public static readonly CenterMemberRole[] ManagementRoles =
    [
        CenterMemberRole.CenterOwner,
        CenterMemberRole.CenterAdmin
    ];

    public static readonly CenterMemberRole[] OwnerRoles =
    [
        CenterMemberRole.CenterOwner
    ];
}
