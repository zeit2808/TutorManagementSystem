namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

public sealed record ResetPasswordRequest(string Email, string Otp, string NewPassword, string ConfirmPassword);
