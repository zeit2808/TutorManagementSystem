using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.GetCenter;

public sealed record GetCenterQuery(Guid CenterId) : IQuery<CenterDto>;

public sealed class GetCenterQueryHandler(ICenterRepository repository)
    : IQueryHandler<GetCenterQuery, CenterDto>
{
    public async Task<Result<CenterDto>> HandleAsync(GetCenterQuery query, CancellationToken cancellationToken)
    {
        var center = await repository.FindAsync(query.CenterId, cancellationToken);
        return center is null
            ? Result<CenterDto>.Failure(CenterErrors.NotFound)
            : Result<CenterDto>.Success(CenterDto.From(center));
    }
}
