using System.Text.RegularExpressions;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;

/// <summary>Self-service profile: the display name and the phone number. Email has its own verified flow.</summary>
public sealed record UpdateMyProfileCommand(Guid UserId, string? DisplayName, string? PhoneNumber,
    string? ClientIp, string? UserAgent, string? TraceId) : ICommand;

public sealed partial class UpdateMyProfileCommandHandler(
    IUserRepository users,
    ISecurityAuditRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateMyProfileCommand>
{
    public const int DisplayNameMaxLength = 100;

    public async Task<Result> HandleAsync(UpdateMyProfileCommand command, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null || !user.IsActive) return Result.Failure(CurrentUserErrors.NotFound);

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName)
            ? null
            : Whitespace().Replace(command.DisplayName.Trim(), " ");
        if (displayName is { Length: > DisplayNameMaxLength } || (displayName?.Any(char.IsControl) ?? false))
            return Result.Failure(IdentityErrors.InvalidDisplayName);

        string? phone = null;
        if (string.IsNullOrWhiteSpace(command.PhoneNumber))
        {
            // Centers reach their staff by phone; only student accounts (created by Google) may have none.
            if (!user.IsStudentAccount) return Result.Failure(IdentityErrors.PhoneNumberRequired);
        }
        else
        {
            if (!RegistrationInput.TryNormalizeVietnamesePhoneNumber(command.PhoneNumber, out var normalized))
                return Result.Failure(IdentityErrors.InvalidPhoneNumber);
            if (!string.Equals(normalized, user.PhoneNumber, StringComparison.Ordinal) &&
                await users.ExistsByPhoneNumberAsync(normalized, cancellationToken))
                return Result.Failure(IdentityErrors.PhoneNumberAlreadyExists);
            phone = normalized;
        }

        if (string.Equals(displayName, user.DisplayName, StringComparison.Ordinal) &&
            string.Equals(phone, user.PhoneNumber, StringComparison.Ordinal))
            return Result.Success();

        user.UpdateProfile(displayName, phone);
        await audit.AddAsync(SecurityAuditEvent.Create(user.Id, SecurityAuditEventTypes.ProfileUpdated,
            clock.UtcNow, command.TraceId, command.ClientIp, command.UserAgent), cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (IdentityDuplicateException)
        {
            return Result.Failure(IdentityErrors.PhoneNumberAlreadyExists);
        }

        return Result.Success();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
