using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.RequestCenterChange;

public sealed record RequestCenterChangeCommand(
    Guid CenterId, Guid UserId, CenterChangeProposal Proposal,
    string Reason, string EvidenceReference) : ICommand<CenterChangeRequestDto>;

public sealed class RequestCenterChangeCommandHandler(
    ICenterRepository centerRepository,
    ICenterChangeRequestRepository requestRepository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RequestCenterChangeCommand, CenterChangeRequestDto>
{
    public async Task<Result<CenterChangeRequestDto>> HandleAsync(
        RequestCenterChangeCommand command, CancellationToken cancellationToken)
    {
        var center = await centerRepository.FindAsync(command.CenterId, cancellationToken);
        if (center is null) return Result<CenterChangeRequestDto>.Failure(CenterErrors.NotFound);
        if (center.Status != CenterStatus.Approved)
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.InvalidStatusTransition);
        if (await requestRepository.HasPendingAsync(command.CenterId, cancellationToken))
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeAlreadyPending);

        var request = CenterChangeRequest.Create(
            center, command.UserId, command.Proposal,
            command.Reason, command.EvidenceReference, clock.UtcNow);
        if (!request.HasChanges)
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeHasNoDifferences);

        await requestRepository.AddAsync(request, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OrganizationDuplicateException)
        {
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeAlreadyPending);
        }

        return Result<CenterChangeRequestDto>.Success(CenterChangeRequestDto.From(request));
    }
}
