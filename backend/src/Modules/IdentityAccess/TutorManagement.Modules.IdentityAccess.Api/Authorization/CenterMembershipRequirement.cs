using Microsoft.AspNetCore.Authorization;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authorization;

public sealed class CenterMembershipRequirement(params CenterMemberRole[] allowedRoles)
    : IAuthorizationRequirement
{
    public IReadOnlyCollection<CenterMemberRole> AllowedRoles { get; } = allowedRoles;
}
