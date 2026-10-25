namespace TutorManagement.Modules.Academics.Application.Subjects.Abstractions;

public interface ISubjectClassGuard
{
    Task<ISubjectClassTransaction> BeginAsync(Guid centerId, Guid subjectId, CancellationToken token);
    Task<bool> HasActiveClassesAsync(Guid centerId, Guid subjectId, CancellationToken token);
}

public interface ISubjectClassTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken token);
}
