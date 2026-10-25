using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Centers.GetMyCenters;

public sealed record GetMyCentersQuery(Guid UserId) : IQuery<List<CenterContextDto>>;

public sealed class GetMyCentersQueryHandler(ICenterAccessService centerAccessService)
    : IQueryHandler<GetMyCentersQuery, List<CenterContextDto>>
{
    public async Task<Result<List<CenterContextDto>>> HandleAsync(
        GetMyCentersQuery query,
        CancellationToken cancellationToken) =>
        Result<List<CenterContextDto>>.Success(
            await centerAccessService.ListAsync(query.UserId, cancellationToken));
}
