using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers.CreateCenter;

public sealed record CreateCenterCommand(
    string LegalName,
    string DisplayName,
    string TaxCode,
    string? BusinessRegistrationNumber,
    string Address,
    string ContactEmail,
    string ContactPhone) : ICommand<CenterDto>;

public sealed class CreateCenterCommandHandler(
    ICenterRepository centerRepository,
    IOrganizationUnitOfWork unitOfWork,
    TutorManagement.Application.Abstractions.Clock.IClock clock)
    : ICommandHandler<CreateCenterCommand, CenterDto>
{
    public async Task<Result<CenterDto>> HandleAsync(
        CreateCenterCommand command,
        CancellationToken cancellationToken)
    {
        var taxCode = command.TaxCode.Trim();
        if (await centerRepository.TaxCodeExistsAsync(taxCode, cancellationToken))
        {
            return Result<CenterDto>.Failure(CenterErrors.TaxCodeAlreadyExists);
        }

        var center = Center.Create(
            command.LegalName,
            command.DisplayName,
            taxCode,
            command.BusinessRegistrationNumber,
            command.Address,
            command.ContactEmail,
            command.ContactPhone,
            clock.UtcNow);
        await centerRepository.AddAsync(center, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (OrganizationDuplicateException)
        {
            return Result<CenterDto>.Failure(CenterErrors.TaxCodeAlreadyExists);
        }

        return Result<CenterDto>.Success(CenterDto.From(center));
    }
}
