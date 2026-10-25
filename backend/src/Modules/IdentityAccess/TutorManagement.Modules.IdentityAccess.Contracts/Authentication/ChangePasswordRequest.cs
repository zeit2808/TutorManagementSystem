namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmPassword);
