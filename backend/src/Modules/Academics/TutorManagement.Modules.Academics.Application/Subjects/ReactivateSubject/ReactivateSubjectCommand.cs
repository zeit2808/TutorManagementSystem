using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Application.Subjects.ReactivateSubject;

public sealed record ReactivateSubjectCommand(Guid CenterId, Guid SubjectId, Guid ActorId,
    int ExpectedVersion) : ICommand<SubjectDto>;

public sealed class ReactivateSubjectCommandHandler(
    ISubjectRepository subjects, IAcademicsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ReactivateSubjectCommand, SubjectDto>
{
    public async Task<Result<SubjectDto>> HandleAsync(ReactivateSubjectCommand command, CancellationToken cancellationToken)
    {
        var subject = await subjects.FindAsync(command.CenterId, command.SubjectId, cancellationToken);
        if (subject is null) return Result<SubjectDto>.Failure(SubjectErrors.NotFound);
        if (subject.Version != command.ExpectedVersion)
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        if (!subject.Reactivate(command.ActorId, clock.UtcNow))
            return Result<SubjectDto>.Failure(SubjectErrors.AlreadyActive);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (SubjectConcurrencyException)
        {
            return Result<SubjectDto>.Failure(SubjectErrors.ConcurrentChange);
        }
        return Result<SubjectDto>.Success(SubjectDto.From(subject));
    }
}
