using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.Modules.IdentityAccess.Application.Centers;

public sealed class CenterAccessService(
    ICenterMembershipReader membershipReader,
    IOrganizationCenterReader centerReader) : ICenterAccessService
{
    // The service is scoped to one HTTP request. The authorization policy and
    // controller can reuse the same verified context without querying twice.
    private readonly Dictionary<(Guid UserId, Guid CenterId), CenterContextDto?> _contexts = [];

    public async Task<CenterContextDto?> FindAsync(
        Guid userId,
        Guid centerId,
        CancellationToken cancellationToken)
    {
        var key = (userId, centerId);
        if (_contexts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var membership = await membershipReader.FindActiveAsync(
            userId,
            centerId,
            cancellationToken);
        if (membership is null)
        {
            _contexts[key] = null;
            return null;
        }

        var center = await centerReader.FindAsync(centerId, cancellationToken);
        var context = center is null ? null : Map(membership, center);
        _contexts[key] = context;
        return context;
    }

    public async Task<List<CenterContextDto>> ListAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var memberships = await membershipReader.ListActiveAsync(userId, cancellationToken);
        if (memberships.Count == 0)
        {
            return [];
        }

        var centers = await centerReader.ListByIdsAsync(
            memberships.Select(membership => membership.CenterId).Distinct().ToArray(),
            cancellationToken);
        var centerById = centers.ToDictionary(center => center.Id);
        var result = new List<CenterContextDto>(memberships.Count);

        foreach (var membership in memberships)
        {
            if (!centerById.TryGetValue(membership.CenterId, out var center))
            {
                continue;
            }

            var context = Map(membership, center);
            _contexts[(userId, membership.CenterId)] = context;
            result.Add(context);
        }

        return result.OrderBy(center => center.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static CenterContextDto Map(
        CenterMembershipSnapshot membership,
        CenterAccessSnapshot center) => new()
        {
            CenterId = center.Id,
            DisplayName = center.DisplayName,
            Role = membership.Role.ToString(),
            Status = center.Status,
            CanAccess = center.IsApproved
        };
}
