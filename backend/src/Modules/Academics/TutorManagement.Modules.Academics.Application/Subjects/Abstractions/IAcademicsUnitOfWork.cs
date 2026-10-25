namespace TutorManagement.Modules.Academics.Application.Subjects.Abstractions;

public interface IAcademicsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
