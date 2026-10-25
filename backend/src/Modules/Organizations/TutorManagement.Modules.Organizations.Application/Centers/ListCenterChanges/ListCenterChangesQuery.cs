using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.ListCenterChanges;

public sealed record ListCenterChangesQuery(Guid CenterId) : IQuery<List<CenterChangeRequestDto>>;

public sealed class ListCenterChangesQueryHandler(
    ICenterRepository centerRepository, ICenterChangeRequestRepository repository)
    : IQueryHandler<ListCenterChangesQuery, List<CenterChangeRequestDto>>
{
    public async Task<Result<List<CenterChangeRequestDto>>> HandleAsync(
        ListCenterChangesQuery query, CancellationToken cancellationToken)
    {
        if (await centerRepository.FindAsync(query.CenterId, cancellationToken) is null)
            return Result<List<CenterChangeRequestDto>>.Failure(CenterErrors.NotFound);
        var requests = await repository.ListAsync(query.CenterId, cancellationToken);
        return Result<List<CenterChangeRequestDto>>.Success(
            requests.Select(CenterChangeRequestDto.From).ToList());
    }
}
