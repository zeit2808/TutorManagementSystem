using Microsoft.EntityFrameworkCore;
using TutorManagement.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Application.Codes;
using TutorManagement.Modules.Organizations.Domain;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Infrastructure.Persistence.Codes;

public sealed class OrganizationCodeGenerator(OrganizationDbContext db) : IOrganizationCodeGenerator
{
    public Task<string> NextAsync(Guid centerId, CodeKind kind, CancellationToken token) =>
        CodeSequence.NextAsync(db.Database, OrganizationDbContext.Schema, centerId, kind, IsTaken(centerId, kind), token);

    private Func<string, CancellationToken, Task<bool>> IsTaken(Guid centerId, CodeKind kind)
    {
        if (kind == OrganizationCodes.Room)
            return (code, token) => db.Rooms.AnyAsync(x => x.CenterId == centerId && x.NormalizedCode == code, token);
        throw new ArgumentOutOfRangeException(nameof(kind), kind.Prefix, "Not a code kind of the Organizations module.");
    }
}
