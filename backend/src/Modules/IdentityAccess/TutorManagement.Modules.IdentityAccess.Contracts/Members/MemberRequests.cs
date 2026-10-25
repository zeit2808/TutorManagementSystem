namespace TutorManagement.Modules.IdentityAccess.Contracts.Members;

public sealed record InviteMemberRequest(string Email, string Role);
public sealed record ChangeMemberRoleRequest(string Role);
public sealed record InvitationTokenRequest(string Token);
