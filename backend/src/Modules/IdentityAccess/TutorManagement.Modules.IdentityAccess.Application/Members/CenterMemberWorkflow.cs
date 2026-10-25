using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Members;

public sealed record CenterMemberDto(Guid MembershipId, Guid UserId, string? Username, string Email,
    string Role, bool IsActive, DateTimeOffset JoinedAtUtc);

public sealed record MemberInvitationDto(Guid Id, string Email, string Role, DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc, bool AccountExists);

public sealed record InvitationPreviewDto(Guid CenterId, string CenterName, string Email, string Role,
    DateTimeOffset ExpiresAtUtc, bool AccountExists);

public sealed record AcceptedMembershipDto(Guid CenterId, string CenterName, string Role);

public static class MemberErrors
{
    public static readonly Error OwnerOnly = new("center_owner_required",
        "Only the center owner can manage members.");
    public static readonly Error InvalidRole = new("invalid_member_role",
        "Role must be CenterAdmin, Teacher or Staff.", "role");
    public static readonly Error InvalidEmail = new("validation_error", "Provide a valid email address.", "email");
    public static readonly Error NotFound = new("center_member_not_found", "The member was not found in this center.");
    public static readonly Error InvitationNotFound = new("member_invitation_not_found",
        "The invitation was not found in this center or is no longer pending.");
    public static readonly Error OwnerProtected = new("center_owner_protected",
        "The owner membership cannot be changed or deactivated here.");
    public static readonly Error AlreadyInState = new("center_member_unchanged", "The member already has this state.");
}

public interface ICenterMemberStore
{
    Task<CenterMembership?> FindMembershipAsync(Guid centerId, Guid userId, CancellationToken token);
    Task<CenterMembership?> FindMembershipByIdAsync(Guid centerId, Guid membershipId, CancellationToken token);
    Task<bool> IsActiveOwnerOfAnyCenterAsync(Guid userId, CancellationToken token);
    Task<IReadOnlyList<CenterMemberDto>> ListMembersAsync(Guid centerId, CancellationToken token);
    Task<IReadOnlyList<MemberInvitationDto>> ListPendingInvitationsAsync(Guid centerId, DateTimeOffset nowUtc,
        CancellationToken token);
    Task<CenterInvitation?> FindInvitationAsync(Guid centerId, Guid invitationId, CancellationToken token);
    /// <summary>Invitations not yet accepted or revoked, including expired ones that can be sent again.</summary>
    Task<IReadOnlyList<MemberInvitationDto>> ListOpenInvitationsAsync(Guid centerId, CancellationToken token);
}

/// <summary>
/// Membership of a center (decision D1/D2): only the owner invites, changes roles and deactivates;
/// owners and admins can see the roster. One account works everywhere — inviting an existing account
/// adds a membership instead of creating another login.
/// </summary>
public sealed class CenterMemberWorkflow(
    ICenterMemberStore store,
    IOrganizationCenterReader centerReader,
    IUserRepository users,
    ICenterInvitationRepository invitations,
    ICenterMembershipRepository memberships,
    IEmailOutboxRepository outbox,
    IOneTimeTokenService tokens,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    IFrontendLinks? links = null)
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(48);

    public Task<IReadOnlyList<CenterMemberDto>> ListAsync(Guid centerId, CancellationToken token) =>
        store.ListMembersAsync(centerId, token);

    public Task<IReadOnlyList<MemberInvitationDto>> ListInvitationsAsync(Guid centerId, CancellationToken token) =>
        store.ListPendingInvitationsAsync(centerId, clock.UtcNow, token);

    public async Task<Result<MemberInvitationDto>> InviteAsync(Guid centerId, Guid actorId, string email,
        string role, CancellationToken token)
    {
        if (!await IsOwnerAsync(centerId, actorId, token)) return Result<MemberInvitationDto>.Failure(MemberErrors.OwnerOnly);
        if (!TryAssignableRole(role, out var memberRole)) return Result<MemberInvitationDto>.Failure(MemberErrors.InvalidRole);
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 254 || !email.Contains('@'))
            return Result<MemberInvitationDto>.Failure(MemberErrors.InvalidEmail);

        var center = await centerReader.FindAsync(centerId, token);
        if (center is null || !center.IsApproved) return Result<MemberInvitationDto>.Failure(IdentityErrors.CenterNotApproved);

        var normalizedEmail = RegistrationInput.NormalizeEmail(email);
        var existing = await users.FindByNormalizedEmailAsync(normalizedEmail, token);
        if (existing is not null)
        {
            if (existing.IsStudentAccount) return Result<MemberInvitationDto>.Failure(IdentityErrors.StudentAccountNotAllowed);
            var membership = await store.FindMembershipAsync(centerId, existing.Id, token);
            if (membership is { IsActive: true }) return Result<MemberInvitationDto>.Failure(IdentityErrors.AlreadyCenterMember);
        }

        var now = clock.UtcNow;
        if (await invitations.HasActiveInvitationAsync(centerId, normalizedEmail, now, token))
            return Result<MemberInvitationDto>.Failure(IdentityErrors.ActiveInvitationAlreadyExists);

        var issued = tokens.Create();
        var invitation = CenterInvitation.CreateMember(centerId, email, normalizedEmail, memberRole, issued.Hash,
            now.Add(InvitationLifetime), now, actorId);
        await invitations.AddAsync(invitation, token);
        await outbox.AddAsync(CenterInvitationMessageFactory.Create(invitation, center.DisplayName, issued.Value,
            existing is not null, now, links?.InvitationUrl(issued.Value)), token);
        try { await unitOfWork.SaveChangesAsync(token); }
        catch (IdentityDuplicateException)
        { return Result<MemberInvitationDto>.Failure(IdentityErrors.ActiveInvitationAlreadyExists); }

        return Result<MemberInvitationDto>.Success(new MemberInvitationDto(invitation.Id, invitation.Email,
            invitation.Role.ToString(), invitation.CreatedAtUtc, invitation.ExpiresAtUtc, existing is not null));
    }

    public async Task<Result<MemberInvitationDto>> RevokeInvitationAsync(Guid centerId, Guid actorId,
        Guid invitationId, CancellationToken token)
    {
        if (!await IsOwnerAsync(centerId, actorId, token)) return Result<MemberInvitationDto>.Failure(MemberErrors.OwnerOnly);
        var invitation = await store.FindInvitationAsync(centerId, invitationId, token);
        var now = clock.UtcNow;
        if (invitation is null || invitation.Role == CenterMemberRole.CenterOwner || !invitation.IsActiveAt(now))
            return Result<MemberInvitationDto>.Failure(MemberErrors.InvitationNotFound);
        invitation.Revoke(now, "Revoked by center owner");
        await unitOfWork.SaveChangesAsync(token);
        return Result<MemberInvitationDto>.Success(new MemberInvitationDto(invitation.Id, invitation.Email,
            invitation.Role.ToString(), invitation.CreatedAtUtc, invitation.ExpiresAtUtc, false));
    }

    /// <summary>Owner sends a member invitation again with a new 48-hour link.</summary>
    public async Task<Result<MemberInvitationDto>> ResendInvitationAsync(Guid centerId, Guid actorId,
        Guid invitationId, CancellationToken token)
    {
        if (!await IsOwnerAsync(centerId, actorId, token)) return Result<MemberInvitationDto>.Failure(MemberErrors.OwnerOnly);
        var invitation = await store.FindInvitationAsync(centerId, invitationId, token);
        if (invitation is null || invitation.Role == CenterMemberRole.CenterOwner)
            return Result<MemberInvitationDto>.Failure(MemberErrors.InvitationNotFound);
        return await ReissueAsync(invitation, token);
    }

    /// <summary>Shared by the owner (member invitations) and the platform (owner invitations).</summary>
    internal async Task<Result<MemberInvitationDto>> ReissueAsync(CenterInvitation invitation, CancellationToken token)
    {
        var now = clock.UtcNow;
        if (invitation.AcceptedAtUtc.HasValue || invitation.RevokedAtUtc.HasValue)
            return Result<MemberInvitationDto>.Failure(MemberErrors.InvitationNotFound);
        if (!invitation.IsActiveAt(now) &&
            await invitations.HasActiveInvitationAsync(invitation.CenterId, invitation.NormalizedEmail, now, token))
            return Result<MemberInvitationDto>.Failure(IdentityErrors.ActiveInvitationAlreadyExists);
        var center = await centerReader.FindAsync(invitation.CenterId, token);
        if (center is null || !center.IsApproved) return Result<MemberInvitationDto>.Failure(IdentityErrors.CenterNotApproved);

        var existing = await users.FindByNormalizedEmailAsync(invitation.NormalizedEmail, token);
        if (existing is { IsStudentAccount: true })
            return Result<MemberInvitationDto>.Failure(IdentityErrors.StudentAccountNotAllowed);
        var issued = tokens.Create();
        invitation.Reissue(issued.Hash, now.Add(InvitationLifetime));
        await outbox.AddAsync(CenterInvitationMessageFactory.Create(invitation, center.DisplayName, issued.Value,
            existing is not null, now, links?.InvitationUrl(issued.Value)), token);
        await unitOfWork.SaveChangesAsync(token);
        return Result<MemberInvitationDto>.Success(new MemberInvitationDto(invitation.Id, invitation.Email,
            invitation.Role.ToString(), invitation.CreatedAtUtc, invitation.ExpiresAtUtc, existing is not null));
    }

    public Task<Result<CenterMemberDto>> ChangeRoleAsync(Guid centerId, Guid actorId, Guid membershipId,
        string role, CancellationToken token) =>
        UpdateMemberAsync(centerId, actorId, membershipId, membership =>
        {
            if (!TryAssignableRole(role, out var memberRole)) return MemberErrors.InvalidRole;
            if (membership.Role == memberRole) return MemberErrors.AlreadyInState;
            membership.ChangeRole(memberRole);
            return Error.None;
        }, token);

    public Task<Result<CenterMemberDto>> DeactivateAsync(Guid centerId, Guid actorId, Guid membershipId,
        CancellationToken token) =>
        UpdateMemberAsync(centerId, actorId, membershipId, membership =>
        {
            if (!membership.IsActive) return MemberErrors.AlreadyInState;
            membership.Deactivate();
            return Error.None;
        }, token);

    public Task<Result<CenterMemberDto>> ReactivateAsync(Guid centerId, Guid actorId, Guid membershipId,
        CancellationToken token) =>
        UpdateMemberAsync(centerId, actorId, membershipId, membership =>
        {
            if (membership.IsActive) return MemberErrors.AlreadyInState;
            membership.Reactivate();
            return Error.None;
        }, token);

    /// <summary>Lets the invitation page show the right form before the person types anything.</summary>
    public async Task<Result<InvitationPreviewDto>> PreviewAsync(string invitationToken, CancellationToken token)
    {
        var invitation = await FindActiveInvitationAsync(invitationToken, token);
        if (invitation is null) return Result<InvitationPreviewDto>.Failure(IdentityErrors.InvalidCenterInvitation);
        var center = await centerReader.FindAsync(invitation.CenterId, token);
        if (center is null || !center.IsApproved) return Result<InvitationPreviewDto>.Failure(IdentityErrors.CenterNotApproved);
        var account = await users.FindByNormalizedEmailAsync(invitation.NormalizedEmail, token);
        if (account is { IsStudentAccount: true })
            return Result<InvitationPreviewDto>.Failure(IdentityErrors.StudentAccountNotAllowed);
        var accountExists = account is not null;
        return Result<InvitationPreviewDto>.Success(new InvitationPreviewDto(invitation.CenterId, center.DisplayName,
            invitation.Email, invitation.Role.ToString(), invitation.ExpiresAtUtc, accountExists));
    }

    /// <summary>
    /// Accepts an invitation with the account that is already signed in. The invitation email must be
    /// this account's email, so the token alone never attaches a center to someone else's login.
    /// </summary>
    public async Task<Result<AcceptedMembershipDto>> AcceptAsSignedInAsync(Guid userId, string invitationToken,
        CancellationToken token)
    {
        var invitation = await FindActiveInvitationAsync(invitationToken, token);
        if (invitation is null) return Result<AcceptedMembershipDto>.Failure(IdentityErrors.InvalidCenterInvitation);
        var center = await centerReader.FindAsync(invitation.CenterId, token);
        if (center is null || !center.IsApproved) return Result<AcceptedMembershipDto>.Failure(IdentityErrors.CenterNotApproved);
        var user = await users.FindByIdAsync(userId, token);
        if (user is null || !user.IsActive) return Result<AcceptedMembershipDto>.Failure(IdentityErrors.InvalidCenterInvitation);
        if (!string.Equals(user.NormalizedEmail, invitation.NormalizedEmail, StringComparison.Ordinal))
            return Result<AcceptedMembershipDto>.Failure(IdentityErrors.InvitationEmailMismatch);
        if (user.IsStudentAccount)
            return Result<AcceptedMembershipDto>.Failure(IdentityErrors.StudentAccountNotAllowed);
        if (invitation.Role == CenterMemberRole.CenterOwner &&
            await store.IsActiveOwnerOfAnyCenterAsync(userId, token))
            return Result<AcceptedMembershipDto>.Failure(IdentityErrors.AlreadyCenterOwner);

        var now = clock.UtcNow;
        var membership = await store.FindMembershipAsync(invitation.CenterId, userId, token);
        if (membership is { IsActive: true })
            return Result<AcceptedMembershipDto>.Failure(IdentityErrors.AlreadyCenterMember);
        if (membership is null)
            await memberships.AddAsync(CenterMembership.Create(invitation.CenterId, userId, invitation.Role, now), token);
        else if (membership.Role == CenterMemberRole.CenterOwner || invitation.Role == CenterMemberRole.CenterOwner)
            return Result<AcceptedMembershipDto>.Failure(IdentityErrors.AlreadyCenterMember);
        else
            membership.Rejoin(invitation.Role);

        // Opening the emailed link proves the address, so an unverified local account becomes verified.
        user.VerifyEmail(now);
        invitation.Accept(userId, now);
        try { await unitOfWork.SaveChangesAsync(token); }
        catch (IdentityDuplicateException duplicate)
        {
            return Result<AcceptedMembershipDto>.Failure(duplicate.ConstraintName.Contains("owner", StringComparison.Ordinal)
                ? IdentityErrors.AlreadyCenterOwner : IdentityErrors.AlreadyCenterMember);
        }

        return Result<AcceptedMembershipDto>.Success(new AcceptedMembershipDto(invitation.CenterId,
            center.DisplayName, invitation.Role.ToString()));
    }

    private async Task<Result<CenterMemberDto>> UpdateMemberAsync(Guid centerId, Guid actorId, Guid membershipId,
        Func<CenterMembership, Error> change, CancellationToken token)
    {
        if (!await IsOwnerAsync(centerId, actorId, token)) return Result<CenterMemberDto>.Failure(MemberErrors.OwnerOnly);
        var membership = await store.FindMembershipByIdAsync(centerId, membershipId, token);
        if (membership is null) return Result<CenterMemberDto>.Failure(MemberErrors.NotFound);
        if (membership.Role == CenterMemberRole.CenterOwner || membership.UserId == actorId)
            return Result<CenterMemberDto>.Failure(MemberErrors.OwnerProtected);
        var error = change(membership);
        if (error != Error.None) return Result<CenterMemberDto>.Failure(error);
        await unitOfWork.SaveChangesAsync(token);
        var roster = await store.ListMembersAsync(centerId, token);
        return Result<CenterMemberDto>.Success(roster.Single(x => x.MembershipId == membershipId));
    }

    private async Task<bool> IsOwnerAsync(Guid centerId, Guid userId, CancellationToken token) =>
        await store.FindMembershipAsync(centerId, userId, token) is
            { IsActive: true, Role: CenterMemberRole.CenterOwner };

    private async Task<CenterInvitation?> FindActiveInvitationAsync(string invitationToken, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(invitationToken) || invitationToken.Length > 200) return null;
        var invitation = await invitations.FindByTokenHashAsync(tokens.Hash(invitationToken.Trim()), token);
        return invitation is not null && invitation.IsActiveAt(clock.UtcNow) ? invitation : null;
    }

    private static bool TryAssignableRole(string? value, out CenterMemberRole role) =>
        Enum.TryParse(value, true, out role) && Enum.GetNames<CenterMemberRole>()
            .Any(name => name.Equals(value, StringComparison.OrdinalIgnoreCase)) &&
        CenterMembership.IsAssignableByOwner(role);
}
