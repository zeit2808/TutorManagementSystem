using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.CreateSubject;

public sealed record CreateSubjectCommand(Guid CenterId, Guid ActorId,
    string Code, string Name, string? Description) : ICommand<SubjectDto>;

public sealed class CreateSubjectCommandHandler(
    ISubjectRepository subjects, IAcademicsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<CreateSubjectCommand, SubjectDto>
{
    public async Task<Result<SubjectDto>> HandleAsync(CreateSubjectCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.ActorId == Guid.Empty ||
            !Subject.AreDetailsValid(command.Code, command.Name, command.Description))
            return Result<SubjectDto>.Failure(SubjectErrors.InvalidDetails);

        var code = Subject.NormalizeCode(command.Code);
        var normalizedName = Subject.NormalizeName(command.Name).ToUpperInvariant();
        if (await subjects.CodeExistsAsync(command.CenterId, code, null, cancellationToken))
            return Result<SubjectDto>.Failure(SubjectErrors.CodeAlreadyExists);
        if (await subjects.NameExistsAsync(command.CenterId, normalizedName, null, cancellationToken))
            return Result<SubjectDto>.Failure(SubjectErrors.NameAlreadyExists);

        var subject = Subject.Create(command.CenterId, code, command.Name, command.Description,
            command.ActorId, clock.UtcNow);
        await subjects.AddAsync(subject, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (SubjectDuplicateException exception)
        {
            return Result<SubjectDto>.Failure(SubjectWriteErrors.FromDuplicate(exception));
        }
        return Result<SubjectDto>.Success(SubjectDto.From(subject));
    }
}
