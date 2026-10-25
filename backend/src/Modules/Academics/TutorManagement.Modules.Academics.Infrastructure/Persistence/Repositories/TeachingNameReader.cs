using Microsoft.EntityFrameworkCore;
using Npgsql;
using TutorManagement.Modules.Academics.Application.Teaching;

namespace TutorManagement.Modules.Academics.Infrastructure.Persistence.Repositories;

public sealed class TeachingNameReader(AcademicsDbContext db) : ITeachingNameReader
{
    public async Task<TeachingNames> ReadAsync(Guid centerId, IEnumerable<Guid> classIds,
        IEnumerable<Guid> teacherIds, IEnumerable<Guid> roomIds, CancellationToken token)
    {
        var classKeys = classIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        var teacherKeys = teacherIds.Where(x => x != Guid.Empty).Distinct().ToArray();
        var roomKeys = roomIds.Where(x => x != Guid.Empty).Distinct().ToArray();

        var classes = classKeys.Length == 0
            ? new Dictionary<Guid, ClassLabel>()
            : await db.Classes.AsNoTracking()
                .Where(x => x.CenterId == centerId && classKeys.Contains(x.Id))
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToDictionaryAsync(x => x.Id, x => new ClassLabel(x.Code, x.Name), token);
        var teachers = teacherKeys.Length == 0
            ? new Dictionary<Guid, string>()
            : await db.Teachers.AsNoTracking()
                .Where(x => x.CenterId == centerId && teacherKeys.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.FullName, token);
        // Rooms belong to the Organizations module; read them through SQL like the other room checks.
        var rooms = roomKeys.Length == 0
            ? new Dictionary<Guid, string>()
            : (await db.Database.SqlQueryRaw<RoomNameRow>(
                    "SELECT id AS \"Id\", name AS \"Name\" FROM organization.rooms WHERE center_id = @center AND id = ANY(@ids)",
                    new NpgsqlParameter("center", centerId), new NpgsqlParameter("ids", roomKeys))
                .ToListAsync(token))
                .ToDictionary(x => x.Id, x => x.Name);

        return new TeachingNames(classes, teachers, rooms);
    }

    private sealed class RoomNameRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
