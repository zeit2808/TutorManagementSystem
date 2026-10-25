using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;

/// <summary>
/// The signed-in account as the client should display it. Roles come from the database, not the
/// access token, so a role granted or removed since sign-in shows up without waiting for a refresh.
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string? Username,
    string Email,
    bool EmailVerified,
    IReadOnlyList<string> Roles,
    bool HasPassword,
    bool HasGoogle);

public interface ICurrentUserReader
{
    Task<CurrentUserDto?> FindActiveAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<CurrentUserDto>;

public static class CurrentUserErrors
{
    public static readonly Error NotFound = new("account_unavailable",
        "This account no longer exists or has been deactivated.");
}

public sealed class GetCurrentUserQueryHandler(ICurrentUserReader reader)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<Result<CurrentUserDto>> HandleAsync(GetCurrentUserQuery query,
        CancellationToken cancellationToken)
    {
        if (query.UserId == Guid.Empty) return Result<CurrentUserDto>.Failure(CurrentUserErrors.NotFound);
        var user = await reader.FindActiveAsync(query.UserId, cancellationToken);
        return user is null
            ? Result<CurrentUserDto>.Failure(CurrentUserErrors.NotFound)
            : Result<CurrentUserDto>.Success(user);
    }
}
