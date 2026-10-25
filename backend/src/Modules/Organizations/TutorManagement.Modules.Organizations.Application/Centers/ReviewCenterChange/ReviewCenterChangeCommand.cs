using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.ReviewCenterChange;

public sealed record ReviewCenterChangeCommand(
    Guid CenterId, Guid RequestId, Guid ReviewerUserId, bool Approve, string ReviewNote)
    : ICommand<CenterChangeRequestDto>;

public sealed class ReviewCenterChangeCommandHandler(
    ICenterRepository centerRepository,
    ICenterChangeRequestRepository requestRepository,
    IOrganizationUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ReviewCenterChangeCommand, CenterChangeRequestDto>
{
    public async Task<Result<CenterChangeRequestDto>> HandleAsync(
        ReviewCenterChangeCommand command, CancellationToken cancellationToken)
    {
        var request = await requestRepository.FindAsync(
            command.CenterId, command.RequestId, cancellationToken);
        if (request is null)
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeNotFound);
        if (request.Status != CenterChangeStatus.Pending)
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.ChangeAlreadyReviewed);

        var center = await centerRepository.FindAsync(command.CenterId, cancellationToken);
        if (center is null) return Result<CenterChangeRequestDto>.Failure(CenterErrors.NotFound);
        var now = clock.UtcNow;
        if (command.Approve)
        {
            if (center.Status != CenterStatus.Approved)
                return Result<CenterChangeRequestDto>.Failure(CenterErrors.InvalidStatusTransition);
            if (!request.MatchesOriginal(center))
                return Result<CenterChangeRequestDto>.Failure(CenterErrors.CenterDetailsChanged);
            if (request.ProposedTaxCode != center.TaxCode &&
                await centerRepository.TaxCodeExistsAsync(request.ProposedTaxCode, cancellationToken))
                return Result<CenterChangeRequestDto>.Failure(CenterErrors.TaxCodeAlreadyExists);

            request.Approve(command.ReviewerUserId, command.ReviewNote, now);
            center.ApplyApprovedChange(request, now);
        }
        else
        {
            request.Reject(command.ReviewerUserId, command.ReviewNote, now);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OrganizationDuplicateException)
        {
            return Result<CenterChangeRequestDto>.Failure(CenterErrors.TaxCodeAlreadyExists);
        }

        return Result<CenterChangeRequestDto>.Success(CenterChangeRequestDto.From(request));
    }
}
