namespace TutorManagement.Modules.Academics.Application.Teaching;

public sealed record ClassLabel(string Code, string Name);

/// <summary>
/// Display names for the identifiers carried by read models, so clients do not have to page
/// through every catalog to render one schedule. Archived records are included on purpose:
/// history must keep showing the name the record had.
/// </summary>
public sealed class TeachingNames
{
    public static readonly TeachingNames Empty = new(
        new Dictionary<Guid, ClassLabel>(), new Dictionary<Guid, string>(), new Dictionary<Guid, string>());

    public TeachingNames(IReadOnlyDictionary<Guid, ClassLabel> classes,
        IReadOnlyDictionary<Guid, string> teachers, IReadOnlyDictionary<Guid, string> rooms)
    {
        Classes = classes;
        Teachers = teachers;
        Rooms = rooms;
    }

    public IReadOnlyDictionary<Guid, ClassLabel> Classes { get; }
    public IReadOnlyDictionary<Guid, string> Teachers { get; }
    public IReadOnlyDictionary<Guid, string> Rooms { get; }

    public ClassLabel? Class(Guid id) => Classes.GetValueOrDefault(id);
    public string? Teacher(Guid? id) => id.HasValue ? Teachers.GetValueOrDefault(id.Value) : null;
    public string? Room(Guid id) => Rooms.GetValueOrDefault(id);
}

public interface ITeachingNameReader
{
    Task<TeachingNames> ReadAsync(Guid centerId, IEnumerable<Guid> classIds,
        IEnumerable<Guid> teacherIds, IEnumerable<Guid> roomIds, CancellationToken token);
}

public static class TeachingNameExtensions
{
    public static SessionDto WithNames(this SessionDto dto, TeachingNames names)
    {
        var label = names.Class(dto.ClassId);
        dto.ClassCode = label?.Code;
        dto.ClassName = label?.Name;
        dto.TeacherName = names.Teacher(dto.TeacherId);
        dto.RoomName = names.Room(dto.RoomId);
        return dto;
    }

    public static ScheduleSeriesDto WithNames(this ScheduleSeriesDto dto, TeachingNames names)
    {
        var label = names.Class(dto.ClassId);
        return dto with
        {
            ClassCode = label?.Code, ClassName = label?.Name,
            TeacherName = names.Teacher(dto.TeacherId), RoomName = names.Room(dto.RoomId)
        };
    }

    public static StudentSessionDto WithNames(this StudentSessionDto dto, TeachingNames names)
    {
        var label = names.Class(dto.ClassId);
        return dto with
        {
            ClassCode = label?.Code, ClassName = label?.Name,
            TeacherName = names.Teacher(dto.TeacherId), RoomName = names.Room(dto.RoomId)
        };
    }

    public static ClassApplicationDto WithNames(this ClassApplicationDto dto, TeachingNames names)
    {
        var label = names.Class(dto.ClassId);
        return dto with { ClassCode = label?.Code, ClassName = label?.Name };
    }

    public static AttendanceSheetDto WithNames(this AttendanceSheetDto dto, TeachingNames names)
    {
        var label = names.Class(dto.ClassId);
        return dto with
        {
            ClassCode = label?.Code, ClassName = label?.Name,
            ActualTeacherName = names.Teacher(dto.ActualTeacherId)
        };
    }
}
