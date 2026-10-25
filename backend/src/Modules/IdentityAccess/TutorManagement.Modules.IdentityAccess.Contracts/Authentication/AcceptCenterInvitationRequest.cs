namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed record AcceptCenterInvitationRequest(
    string Token,
    string Username,
    string Password,
    string PhoneNumber);
