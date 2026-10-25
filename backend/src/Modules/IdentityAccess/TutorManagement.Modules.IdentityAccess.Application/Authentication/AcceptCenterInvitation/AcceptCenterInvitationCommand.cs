using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.AcceptCenterInvitation;

public sealed record AcceptCenterInvitationCommand(
    string Token,
    string Username,
    string Password,
    string PhoneNumber) : ICommand<AcceptedInvitationResult>;

public sealed class AcceptedInvitationResult
{
    public AcceptedInvitationResult() { }

    public AcceptedInvitationResult(Guid userId, Guid centerId, string email, string role)
    {
        UserId = userId;
        CenterId = centerId;
        Email = email;
        Role = role;
    }

    public Guid UserId { get; set; }
    public Guid CenterId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class AcceptCenterInvitationCommandHandler(
    ICenterInvitationRepository invitationRepository,
    ICenterMembershipRepository membershipRepository,
    IUserRepository userRepository,
    IOrganizationCenterReader centerReader,
    IOneTimeTokenService oneTimeTokenService,
    IPasswordService passwordService,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<AcceptCenterInvitationCommand, AcceptedInvitationResult>
{
    public async Task<Result<AcceptedInvitationResult>> HandleAsync(
        AcceptCenterInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var invitation = await invitationRepository.FindByTokenHashAsync(
            oneTimeTokenService.Hash(command.Token), cancellationToken);
        if (invitation is null || !invitation.IsActiveAt(nowUtc))
        {
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.InvalidCenterInvitation);
        }

        var center = await centerReader.FindAsync(invitation.CenterId, cancellationToken);
        if (center is null || !center.IsApproved)
        {
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.CenterNotApproved);
        }

        var normalizedUsername = RegistrationInput.NormalizeUsername(command.Username);
        if (await userRepository.ExistsByNormalizedUsernameAsync(normalizedUsername, cancellationToken))
        {
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.UsernameAlreadyExists);
        }

        if (await userRepository.ExistsByNormalizedEmailAsync(invitation.NormalizedEmail, cancellationToken))
        {
            // The account exists: it must sign in and accept, so no second login is created.
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.InvitationRequiresSignIn);
        }

        if (!RegistrationInput.TryNormalizeVietnamesePhoneNumber(
            command.PhoneNumber, out var normalizedPhoneNumber))
        {
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.InvalidPhoneNumber);
        }

        if (await userRepository.ExistsByPhoneNumberAsync(normalizedPhoneNumber, cancellationToken))
        {
            return Result<AcceptedInvitationResult>.Failure(IdentityErrors.PhoneNumberAlreadyExists);
        }

        var user = User.Create(
            command.Username,
            invitation.Email,
            normalizedPhoneNumber,
            passwordService.Hash(command.Password),
            nowUtc);
        user.VerifyEmail(nowUtc);
        var membership = CenterMembership.Create(invitation.CenterId, user.Id, invitation.Role, nowUtc);
        invitation.Accept(user.Id, nowUtc);

        await userRepository.AddAsync(user, cancellationToken);
        await membershipRepository.AddAsync(membership, cancellationToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityDuplicateException duplicate)
        {
            return Result<AcceptedInvitationResult>.Failure(MapDuplicate(duplicate.ConstraintName));
        }

        return Result<AcceptedInvitationResult>.Success(new AcceptedInvitationResult(
            user.Id,
            invitation.CenterId,
            user.Email,
            membership.Role.ToString()));
    }

    private static Error MapDuplicate(string constraintName)
    {
        if (constraintName.Contains("username", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityErrors.UsernameAlreadyExists;
        }

        if (constraintName.Contains("phone", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityErrors.PhoneNumberAlreadyExists;
        }

        if (constraintName.Contains("active_owner", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityErrors.AlreadyCenterOwner;
        }

        if (constraintName.Contains("invitation", StringComparison.OrdinalIgnoreCase))
        {
            return IdentityErrors.InvalidCenterInvitation;
        }

        return IdentityErrors.EmailAlreadyExists;
    }
}
