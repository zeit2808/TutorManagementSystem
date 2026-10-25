using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.Modules.Academics.Application.Subjects.Abstractions;

public interface ISubjectRepository
{
    Task<Subject?> FindAsync(Guid centerId, Guid subjectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Subject>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId, CancellationToken cancellationToken);
    Task AddAsync(Subject subject, CancellationToken cancellationToken);
}
