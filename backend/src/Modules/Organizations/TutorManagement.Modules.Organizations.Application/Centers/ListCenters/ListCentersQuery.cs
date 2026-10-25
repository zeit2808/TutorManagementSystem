using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.ListCenters;

public sealed record ListCentersQuery : IQuery<List<CenterDto>>;

public sealed class ListCentersQueryHandler(ICenterRepository centerRepository)
    : IQueryHandler<ListCentersQuery, List<CenterDto>>
{
    public async Task<Result<List<CenterDto>>> HandleAsync(
        ListCentersQuery query,
        CancellationToken cancellationToken)
    {
        var centers = await centerRepository.ListAsync(cancellationToken);
        return Result<List<CenterDto>>.Success(
            centers.Select(CenterDto.From).ToList());
    }
}
