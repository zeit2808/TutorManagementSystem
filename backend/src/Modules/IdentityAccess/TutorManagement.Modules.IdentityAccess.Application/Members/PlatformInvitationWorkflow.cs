using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Members;

/// <summary>
/// The platform's view of a center's invitations. Admins see every open invitation but only manage the
/// owner invitation they sent; member invitations belong to the center owner (D1).
/// </summary>
public sealed class PlatformInvitationWorkflow(
    ICenterMemberStore store,
    CenterMemberWorkflow members,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
{
    public Task<IReadOnlyList<MemberInvitationDto>> ListAsync(Guid centerId, CancellationToken token) =>
        store.ListOpenInvitationsAsync(centerId, token);

    public async Task<Result<MemberInvitationDto>> RevokeAsync(Guid centerId, Guid invitationId,
        CancellationToken token)
    {
        var invitation = await FindOwnerInvitationAsync(centerId, invitationId, token);
        if (invitation is null) return Result<MemberInvitationDto>.Failure(MemberErrors.InvitationNotFound);
        invitation.Revoke(clock.UtcNow, "Revoked by platform administrator");
        await unitOfWork.SaveChangesAsync(token);
        return Result<MemberInvitationDto>.Success(new MemberInvitationDto(invitation.Id, invitation.Email,
            invitation.Role.ToString(), invitation.CreatedAtUtc, invitation.ExpiresAtUtc, false));
    }

    public async Task<Result<MemberInvitationDto>> ResendAsync(Guid centerId, Guid invitationId,
        CancellationToken token)
    {
        var invitation = await FindOwnerInvitationAsync(centerId, invitationId, token);
        return invitation is null
            ? Result<MemberInvitationDto>.Failure(MemberErrors.InvitationNotFound)
            : await members.ReissueAsync(invitation, token);
    }

    private async Task<CenterInvitation?> FindOwnerInvitationAsync(Guid centerId, Guid invitationId,
        CancellationToken token)
    {
        var invitation = await store.FindInvitationAsync(centerId, invitationId, token);
        return invitation is { Role: CenterMemberRole.CenterOwner, AcceptedAtUtc: null, RevokedAtUtc: null }
            ? invitation
            : null;
    }
}
