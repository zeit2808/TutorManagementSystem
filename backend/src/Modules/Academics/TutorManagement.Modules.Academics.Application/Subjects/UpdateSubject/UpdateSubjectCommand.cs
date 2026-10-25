using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.UpdateSubject;

public sealed record UpdateSubjectCommand(Guid CenterId, Guid SubjectId, Guid ActorId, int ExpectedVersion,
    string Code, string Name, string? Description) : ICommand<SubjectDto>;

public sealed class UpdateSubjectCommandHandler(
    ISubjectRepository subjects, IAcademicsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdateSubjectCommand, SubjectDto>
{
    public async Task<Result<SubjectDto>> HandleAsync(UpdateSubjectCommand command, CancellationToken cancellationToken)
    {
        if (command.CenterId == Guid.Empty || command.SubjectId == Guid.Empty || command.ActorId == Guid.Empty ||
            !Subject.AreDetailsValid(command.Code, command.Name, command.Description))
            return Result<SubjectDto>.Failure(SubjectErrors.InvalidDetails);

        var subject = await subjects.FindAsync(command.CenterId, command.SubjectId, cancellationToken);
        if (subject is null) return Result<SubjectDto>.Failure(SubjectErrors.NotFound);
        if (subject.Version != command.ExpectedVersion)
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        if (!subject.IsActive) return Result<SubjectDto>.Failure(SubjectErrors.ArchivedCannotEdit);

        var code = Subject.NormalizeCode(command.Code);
        var normalizedName = Subject.NormalizeName(command.Name).ToUpperInvariant();
        if (await subjects.CodeExistsAsync(command.CenterId, code, subject.Id, cancellationToken))
            return Result<SubjectDto>.Failure(SubjectErrors.CodeAlreadyExists);
        if (await subjects.NameExistsAsync(command.CenterId, normalizedName, subject.Id, cancellationToken))
            return Result<SubjectDto>.Failure(SubjectErrors.NameAlreadyExists);

        if (!subject.Update(command.Code, command.Name, command.Description, command.ActorId, clock.UtcNow))
            return Result<SubjectDto>.Success(SubjectDto.From(subject));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (SubjectDuplicateException exception)
        {
            return Result<SubjectDto>.Failure(SubjectWriteErrors.FromDuplicate(exception));
        }
        catch (SubjectConcurrencyException)
        {
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        }
        return Result<SubjectDto>.Success(SubjectDto.From(subject));
    }
}
