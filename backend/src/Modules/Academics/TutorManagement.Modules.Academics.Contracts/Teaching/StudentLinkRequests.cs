namespace TutorManagement.Modules.Academics.Contracts.Teaching;

/// <summary>M3: validDays is 1, 7 (default) or 30; maxUses is optional (1–500).</summary>
public sealed record CreateClassInviteRequest(int? ValidDays = null, int? MaxUses = null);
/// <summary>Merge sourceStudentId into the profile in the URL (the target keeps its code).</summary>
public sealed record MergeStudentRequest(Guid SourceStudentId);
/// <summary>
/// fullName and parent contact are required only when the login has no profile at this center yet.
/// studiedHereBefore = true without a linked profile is refused: use the personal activation link.
/// </summary>
public sealed record JoinClassInviteRequest(string? Code, string? FullName, string? ParentName, string? ParentPhone,
    bool? StudiedHereBefore = null, bool? AcknowledgeConflicts = null);
