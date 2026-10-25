namespace TutorManagement.Modules.Academics.Contracts.Subjects;

public sealed record CreateSubjectRequest(string Code, string Name, string? Description);
