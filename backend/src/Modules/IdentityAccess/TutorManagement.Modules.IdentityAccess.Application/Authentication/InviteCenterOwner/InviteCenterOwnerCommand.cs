using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.InviteCenterOwner;

public sealed record InviteCenterOwnerCommand(Guid CenterId, string Email, Guid InvitedByUserId)
    : ICommand<CenterInvitationResult>;

public sealed class CenterInvitationResult
{
    public CenterInvitationResult() { }

    public CenterInvitationResult(
        Guid invitationId,
        Guid centerId,
        string email,
        string role,
        DateTimeOffset expiresAtUtc)
    {
        InvitationId = invitationId;
        CenterId = centerId;
        Email = email;
        Role = role;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid InvitationId { get; set; }
    public Guid CenterId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class InviteCenterOwnerCommandHandler(
    IOrganizationCenterReader centerReader,
    IPlatformAdminReader platformAdminReader,
    IUserRepository userRepository,
    ICenterInvitationRepository invitationRepository,
    IEmailOutboxRepository outboxRepository,
    IOneTimeTokenService oneTimeTokenService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock,
    ICenterMemberStore? ownerReader = null,
    IFrontendLinks? links = null) : ICommandHandler<InviteCenterOwnerCommand, CenterInvitationResult>
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromHours(48);

    public async Task<Result<CenterInvitationResult>> HandleAsync(
        InviteCenterOwnerCommand command,
        CancellationToken cancellationToken)
    {
        if (!await platformAdminReader.IsActiveAsync(command.InvitedByUserId, cancellationToken))
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.InvalidInviterSession);
        }

        var center = await centerReader.FindAsync(command.CenterId, cancellationToken);
        if (center is null || !center.IsApproved)
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.CenterNotApproved);
        }

        var normalizedEmail = RegistrationInput.NormalizeEmail(command.Email);
        // An existing account may become the owner (it accepts while signed in), unless it already owns a center.
        var existing = await userRepository.FindByNormalizedEmailAsync(normalizedEmail, cancellationToken);
        if (existing is { IsStudentAccount: true })
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.StudentAccountNotAllowed);
        }
        if (existing is not null && ownerReader is not null &&
            await ownerReader.IsActiveOwnerOfAnyCenterAsync(existing.Id, cancellationToken))
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.AlreadyCenterOwner);
        }

        var nowUtc = clock.UtcNow;
        if (await invitationRepository.HasActiveInvitationAsync(
            command.CenterId, normalizedEmail, nowUtc, cancellationToken))
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.ActiveInvitationAlreadyExists);
        }

        var issuedToken = oneTimeTokenService.Create();
        var invitation = CenterInvitation.CreateOwner(
            command.CenterId,
            command.Email,
            normalizedEmail,
            issuedToken.Hash,
            nowUtc.Add(InvitationLifetime),
            nowUtc,
            command.InvitedByUserId);

        await invitationRepository.AddAsync(invitation, cancellationToken);
        await outboxRepository.AddAsync(
            CenterInvitationMessageFactory.Create(
                invitation, center.DisplayName, issuedToken.Value, existing is not null, nowUtc,
                links?.InvitationUrl(issuedToken.Value)),
            cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityDuplicateException)
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.ActiveInvitationAlreadyExists);
        }
        catch (IdentityReferenceException exception)
            when (exception.ConstraintName == "FK_center_invitations_users_invited_by_user_id")
        {
            return Result<CenterInvitationResult>.Failure(IdentityErrors.InvalidInviterSession);
        }

        return Result<CenterInvitationResult>.Success(new CenterInvitationResult(
            invitation.Id,
            invitation.CenterId,
            invitation.Email,
            invitation.Role.ToString(),
            invitation.ExpiresAtUtc));
    }
}
