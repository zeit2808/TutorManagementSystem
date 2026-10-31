using Microsoft.EntityFrameworkCore;
using TutorManagement.Infrastructure.Persistence;
using TutorManagement.Modules.Academics.Application.Codes;
using TutorManagement.Modules.Academics.Domain;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Codes;

public sealed class AcademicsCodeGenerator(AcademicsDbContext db) : IAcademicsCodeGenerator
{
    public Task<string> NextAsync(Guid centerId, CodeKind kind, CancellationToken token) =>
        CodeSequence.NextAsync(db.Database, AcademicsDbContext.Schema, centerId, kind, IsTaken(centerId, kind), token);

    private Func<string, CancellationToken, Task<bool>> IsTaken(Guid centerId, CodeKind kind)
    {
        if (kind == AcademicsCodes.Student)
            return (code, token) => db.Students.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);
        if (kind == AcademicsCodes.Teacher)
            return (code, token) => db.Teachers.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);
        if (kind == AcademicsCodes.Class)
            return (code, token) => db.Classes.AnyAsync(x => x.CenterId == centerId && x.Code == code, token);
        if (kind == AcademicsCodes.Subject)
            return (code, token) => db.Subjects.AnyAsync(x => x.CenterId == centerId && x.NormalizedCode == code, token);
        throw new ArgumentOutOfRangeException(nameof(kind), kind.Prefix, "Not a code kind of the Academics module.");
    }
}
