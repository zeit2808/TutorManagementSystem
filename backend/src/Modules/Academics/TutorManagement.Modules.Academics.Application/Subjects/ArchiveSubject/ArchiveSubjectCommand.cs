using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.ArchiveSubject;

public sealed record ArchiveSubjectCommand(Guid CenterId, Guid SubjectId, Guid ActorId,
    int ExpectedVersion) : ICommand<SubjectDto>;

public sealed class ArchiveSubjectCommandHandler(
    ISubjectRepository subjects, IAcademicsUnitOfWork unitOfWork, ISubjectClassGuard classGuard, IClock clock)
    : ICommandHandler<ArchiveSubjectCommand, SubjectDto>
{
    public async Task<Result<SubjectDto>> HandleAsync(ArchiveSubjectCommand command, CancellationToken cancellationToken)
    {
        await using var guard = await classGuard.BeginAsync(command.CenterId, command.SubjectId, cancellationToken);
        var subject = await subjects.FindAsync(command.CenterId, command.SubjectId, cancellationToken);
        if (subject is null) return Result<SubjectDto>.Failure(SubjectErrors.NotFound);
        if (subject.Version != command.ExpectedVersion)
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        if (await classGuard.HasActiveClassesAsync(command.CenterId, command.SubjectId, cancellationToken))
            return Result<SubjectDto>.Failure(SubjectErrors.HasActiveClasses);
        if (!subject.Archive(command.ActorId, clock.UtcNow))
            return Result<SubjectDto>.Failure(SubjectErrors.AlreadyArchived);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (SubjectConcurrencyException)
        {
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        }
        await guard.CommitAsync(cancellationToken);
        return Result<SubjectDto>.Success(SubjectDto.From(subject));
    }
}
