using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.Organizations.Application.Centers;

namespace TutorManagement.Modules.Organizations.Api.Centers;

internal static class ChangeRequestNames
{
    public static async Task<List<CenterChangeRequestDto>> FillAsync(this IUserDirectory users,
        List<CenterChangeRequestDto> items, CancellationToken cancellationToken)
    {
        var names = await users.GetDisplayNamesAsync(items.Select(x => x.RequestedByUserId)
            .Concat(items.Where(x => x.ReviewedByUserId.HasValue).Select(x => x.ReviewedByUserId!.Value)),
            cancellationToken);
        foreach (var item in items)
        {
            item.RequestedByName = names.GetValueOrDefault(item.RequestedByUserId);
            item.ReviewedByName = item.ReviewedByUserId is { } reviewer ? names.GetValueOrDefault(reviewer) : null;
        }
        return items;
    }
}
