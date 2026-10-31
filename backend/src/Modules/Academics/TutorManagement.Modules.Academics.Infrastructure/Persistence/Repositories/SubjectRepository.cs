using Microsoft.EntityFrameworkCore;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class SubjectRepository(AcademicsDbContext dbContext) : ISubjectRepository
{
    public Task<Subject?> FindAsync(Guid centerId, Guid subjectId, CancellationToken cancellationToken) =>
        dbContext.Subjects.SingleOrDefaultAsync(
            subject => subject.CenterId == centerId && subject.Id == subjectId, cancellationToken);

    public async Task<IReadOnlyList<Subject>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
        await dbContext.Subjects.AsNoTracking()
            .Where(subject => subject.CenterId == centerId && (includeArchived || subject.ArchivedAtUtc == null))
            .OrderBy(subject => subject.NormalizedName).ThenBy(subject => subject.NormalizedCode)
            .ToArrayAsync(cancellationToken);


    public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
        CancellationToken cancellationToken) =>
        dbContext.Subjects.AnyAsync(subject => subject.CenterId == centerId &&
            subject.NormalizedName == normalizedName && (!excludingId.HasValue || subject.Id != excludingId.Value),
            cancellationToken);

    public async Task AddAsync(Subject subject, CancellationToken cancellationToken) =>
        await dbContext.Subjects.AddAsync(subject, cancellationToken);
}
