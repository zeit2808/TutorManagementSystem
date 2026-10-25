using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.Modules.IdentityAccess.Contracts.Authorization;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Api.Authorization;

public sealed class CenterMembershipAuthorizationHandler(ICenterAccessService centerAccessService)
    : AuthorizationHandler<CenterMembershipRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CenterMembershipRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext ||
            context.User.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId) ||
            !Guid.TryParse(httpContext.Request.RouteValues["centerId"]?.ToString(), out var centerId))
        {
            return;
        }

        var center = await centerAccessService.FindAsync(
            userId,
            centerId,
            httpContext.RequestAborted);

        if (center is { CanAccess: true } &&
            Enum.TryParse<CenterMemberRole>(center.Role, out var role) &&
            requirement.AllowedRoles.Contains(role))
        {
            httpContext.Items[CenterAccessPolicyNames.RoleItemKey] = role.ToString();
            context.Succeed(requirement);
        }
    }
}
