using TutorManagement.Modules.Academics.Domain.Subjects;

namespace TutorManagement.Modules.Academics.Application.Subjects;

public sealed class SubjectDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }

    public static SubjectDto From(Subject subject) => new()
    {
        Id = subject.Id,
        Code = subject.Code,
        Name = subject.Name,
        Description = subject.Description,
        IsActive = subject.IsActive,
        Version = subject.Version
    };
}
