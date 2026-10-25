namespace TutorManagement.Modules.Academics.Contracts.Subjects;

public sealed record UpdateSubjectRequest(string Code, string Name, string? Description, int? ExpectedVersion);
