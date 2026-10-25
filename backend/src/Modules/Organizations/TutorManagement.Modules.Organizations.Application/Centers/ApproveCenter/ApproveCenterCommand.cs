using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.ApproveCenter;

public sealed record ApproveCenterCommand(Guid CenterId, Guid ReviewerUserId) : ICommand<CenterDto>;

public sealed class ApproveCenterCommandHandler(
    ICenterRepository centerRepository,
    IOrganizationUnitOfWork unitOfWork,
    TutorManagement.Application.Abstractions.Clock.IClock clock)
    : ICommandHandler<ApproveCenterCommand, CenterDto>
{
    public async Task<Result<CenterDto>> HandleAsync(
        ApproveCenterCommand command,
        CancellationToken cancellationToken)
    {
        var center = await centerRepository.FindAsync(command.CenterId, cancellationToken);
        if (center is null)
        {
            return Result<CenterDto>.Failure(CenterErrors.NotFound);
        }

        try
        {
            center.Approve(command.ReviewerUserId, clock.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return Result<CenterDto>.Failure(CenterErrors.InvalidStatusTransition);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<CenterDto>.Success(CenterDto.From(center));
    }
}
