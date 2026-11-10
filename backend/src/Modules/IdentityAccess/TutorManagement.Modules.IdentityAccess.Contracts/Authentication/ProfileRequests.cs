namespace TutorManagement.Modules.IdentityAccess.Contracts.Authentication;

/// <summary>Self-service profile. An empty display name clears it; staff accounts must keep a phone number.</summary>
public sealed record UpdateMyProfileRequest(string? DisplayName, string? PhoneNumber);

/// <summary>Step 1 of changing the sign-in email: confirm the password, a code goes to the new address.</summary>
public sealed record RequestEmailChangeRequest(string NewEmail, string CurrentPassword);

/// <summary>Step 2: the eight-digit code that was sent to <see cref="NewEmail"/>.</summary>
public sealed record ConfirmEmailChangeRequest(string NewEmail, string Otp);

public sealed record EmailChangeStartedResponse(string NewEmail, DateTimeOffset ExpiresAtUtc, DateTimeOffset ResendAvailableAtUtc);
