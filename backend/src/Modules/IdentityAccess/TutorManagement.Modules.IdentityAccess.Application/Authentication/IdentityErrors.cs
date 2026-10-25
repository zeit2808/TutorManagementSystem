using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public static class IdentityErrors
{
    public static readonly Error InvalidGoogleToken = new("invalid_google_token", "The Google ID token is invalid or expired.");
    public static readonly Error GoogleAccountLinkRequired = new("google_account_link_required",
        "An account already uses this email. Sign in to that account before linking Google.");
    public static readonly Error GoogleAlreadyLinked = new("google_account_already_linked",
        "This Google identity is linked to another account.");
    public static readonly TutorManagement.SharedKernel.Primitives.Error PasswordNotConfigured =
        new("password_not_configured", "This account does not have a local password.");
    public static readonly Error InvalidInviterSession = new(
        "invalid_inviter_session",
        "The platform administrator session is no longer valid. Please sign in again.");

    public static readonly Error InvalidCredentials = new(
        "invalid_credentials",
        "Invalid username or password.");

    public static readonly Error InvalidRefreshToken = new(
        "invalid_refresh_token",
        "The refresh token is invalid or expired.");

    public static readonly Error InvalidCurrentPassword = new(
        "invalid_current_password", "The current password is incorrect.", "currentPassword");

    public static readonly Error PasswordUnchanged = new(
        "password_unchanged", "The new password must differ from the current password.", "newPassword");

    public static readonly Error WeakPassword = new(
        "weak_password", "The new password does not meet the password policy.", "newPassword");

    public static readonly Error InvalidPasswordResetOtp = new(
        "invalid_password_reset_otp", "The reset code is invalid, expired, or has already been used.", "otp");

    public static readonly Error EmailNotVerified = new(
        "email_not_verified",
        "The email address has not been verified.");

    public static readonly Error UsernameAlreadyExists = new(
        "username_already_exists",
        "The username is already in use.",
        "username");

    public static readonly Error EmailAlreadyExists = new(
        "email_already_exists",
        "The email address is already in use.",
        "email");

    public static readonly Error PhoneNumberAlreadyExists = new(
        "phone_number_already_exists",
        "The phone number is already in use.",
        "phoneNumber");

    public static readonly Error InvalidPhoneNumber = new(
        "invalid_phone_number",
        "The phone number is invalid.",
        "phoneNumber");

    public static readonly Error InvalidEmailVerificationToken = new(
        "invalid_email_verification_token",
        "The email verification token is invalid, expired, or has already been used.");

    public static readonly Error CenterNotApproved = new(
        "center_not_approved",
        "The center does not exist or has not been approved.");

    public static readonly Error ActiveInvitationAlreadyExists = new(
        "active_invitation_already_exists",
        "An active invitation already exists for this email and center.",
        "email");

    public static readonly Error InvalidCenterInvitation = new(
        "invalid_center_invitation",
        "The invitation is invalid, expired, revoked, or has already been used.");

    public static readonly Error AlreadyCenterOwner = new(
        "already_center_owner",
        "This account already owns a center. An account can own at most one center.",
        "email");

    public static readonly Error StudentAccountNotAllowed = new(
        "student_account_not_allowed",
        "This email belongs to a student account. Student accounts cannot join a center as staff; use another email.");
    public static readonly Error InvitationRequiresSignIn = new(
        "invitation_requires_sign_in",
        "This email already has an account. Sign in with it, then accept the invitation.");

    public static readonly Error InvitationEmailMismatch = new(
        "invitation_email_mismatch",
        "This invitation was sent to a different email than the signed-in account.");

    public static readonly Error AlreadyCenterMember = new(
        "already_center_member",
        "This account is already an active member of the center.",
        "email");
}
