using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.RejectCenter;

public sealed record RejectCenterCommand(Guid CenterId, Guid ReviewerUserId, string Reason)
    : ICommand<CenterDto>;

public sealed class RejectCenterCommandHandler(
    ICenterRepository centerRepository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RejectCenterCommand, CenterDto>
{
    public async Task<Result<CenterDto>> HandleAsync(
        RejectCenterCommand command,
        CancellationToken cancellationToken)
    {
        var center = await centerRepository.FindAsync(command.CenterId, cancellationToken);
        if (center is null)
        {
            return Result<CenterDto>.Failure(CenterErrors.NotFound);
        }

        try
        {
            center.Reject(command.ReviewerUserId, command.Reason, clock.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<CenterDto>.Failure(CenterErrors.InvalidStatusTransition);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<CenterDto>.Success(CenterDto.From(center));
    }
}
