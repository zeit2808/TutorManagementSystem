using TutorManagement.SharedKernel.Domain;

namespace TutorManagement.Modules.IdentityAccess.Domain.Authentication;

public sealed class User : AggregateRoot<Guid>
{
    private readonly List<UserRole> _userRoles = [];

    private User()
        : base(Guid.Empty)
    {
    }

    private User(
        Guid id,
        string? username,
        string? normalizedUsername,
        string email,
        string normalizedEmail,
        string? phoneNumber,
        string? passwordHash,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Username = username;
        NormalizedUsername = normalizedUsername;
        Email = email;
        NormalizedEmail = normalizedEmail;
        PhoneNumber = phoneNumber;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public string? Username { get; private set; }

    public string? NormalizedUsername { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string? PhoneNumber { get; private set; }

    public string? PasswordHash { get; private set; }

    public bool HasLocalPassword => !string.IsNullOrWhiteSpace(PasswordHash);

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? EmailVerifiedAtUtc { get; private set; }

    public bool IsEmailVerified => EmailVerifiedAtUtc.HasValue;

    public IReadOnlyCollection<UserRole> UserRoles => _userRoles;

    /// <summary>
    /// Student accounts and staff accounts are separate kinds: an account created by Google sign-in is a student
    /// account and can never join a center as staff; a staff account never becomes a student. Requires
    /// <see cref="UserRoles"/> to be loaded.
    /// </summary>
    public bool IsStudentAccount => _userRoles.Any(userRole => userRole.RoleId == SystemRoles.StudentId);

    public static User Create(
        string username,
        string email,
        string phoneNumber,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var trimmedUsername = username.Trim();
        var trimmedEmail = email.Trim();

        return new User(
            Guid.NewGuid(),
            trimmedUsername,
            trimmedUsername.ToUpperInvariant(),
            trimmedEmail,
            trimmedEmail.ToUpperInvariant(),
            phoneNumber.Trim(),
            passwordHash,
            createdAtUtc);
    }

    public static User CreateExternal(string email, DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        return new User(Guid.NewGuid(), null, null, trimmed, trimmed.ToUpperInvariant(),
            null, null, createdAtUtc);
    }

    public void AssignRole(Guid roleId)
    {
        if (_userRoles.Any(userRole => userRole.RoleId == roleId))
        {
            return;
        }

        _userRoles.Add(new UserRole(Id, roleId));
    }

    public void ChangePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void VerifyEmail(DateTimeOffset verifiedAtUtc)
    {
        EmailVerifiedAtUtc ??= verifiedAtUtc;
    }

    public void Deactivate() => IsActive = false;
}
