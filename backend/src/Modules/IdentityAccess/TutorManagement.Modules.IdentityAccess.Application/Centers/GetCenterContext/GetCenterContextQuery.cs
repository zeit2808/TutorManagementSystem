using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Centers.GetCenterContext;

public sealed record GetCenterContextQuery(Guid UserId, Guid CenterId)
    : IQuery<CenterContextDto>;

public sealed class GetCenterContextQueryHandler(ICenterAccessService centerAccessService)
    : IQueryHandler<GetCenterContextQuery, CenterContextDto>
{
    public async Task<Result<CenterContextDto>> HandleAsync(
        GetCenterContextQuery query,
        CancellationToken cancellationToken)
    {
        var center = await centerAccessService.FindAsync(
            query.UserId,
            query.CenterId,
            cancellationToken);

        return center is { CanAccess: true }
            ? Result<CenterContextDto>.Success(center)
            : Result<CenterContextDto>.Failure(new Error(
                "center_access_denied",
                "You do not have access to this center."));
    }
}
