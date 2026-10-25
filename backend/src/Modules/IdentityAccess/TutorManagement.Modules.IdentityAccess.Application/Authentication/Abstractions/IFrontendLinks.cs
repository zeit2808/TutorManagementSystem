namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;

/// <summary>Builds links into the web app for emails. Returns null when no front-end address is configured.</summary>
public interface IFrontendLinks
{
    string? InvitationUrl(string invitationToken);

    string? VerifyEmailUrl(string verificationToken) => null;
}
