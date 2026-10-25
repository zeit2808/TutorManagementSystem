using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.ReinstateCenter;

public sealed record ReinstateCenterCommand(Guid CenterId, Guid ReviewerUserId, string Reason)
    : ICommand<CenterDto>;

public sealed class ReinstateCenterCommandHandler(
    ICenterRepository centerRepository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ReinstateCenterCommand, CenterDto>
{
    public async Task<Result<CenterDto>> HandleAsync(
        ReinstateCenterCommand command,
        CancellationToken cancellationToken)
    {
        var center = await centerRepository.FindAsync(command.CenterId, cancellationToken);
        if (center is null)
        {
            return Result<CenterDto>.Failure(CenterErrors.NotFound);
        }

        try
        {
            center.Reinstate(command.ReviewerUserId, command.Reason, clock.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<CenterDto>.Failure(CenterErrors.InvalidStatusTransition);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<CenterDto>.Success(CenterDto.From(center));
    }
}
