namespace TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;

public static class PasswordPolicy
{
    private static readonly string[] ForbiddenFragments = ["password", "123456", "qwerty", "admin123"];

    public static bool IsValid(string? password) =>
        password is { Length: >= 12 and <= 128 } &&
        password.Any(char.IsUpper) &&
        password.Any(char.IsLower) &&
        password.Any(char.IsDigit) &&
        password.Any(character => !char.IsLetterOrDigit(character)) &&
        !ForbiddenFragments.Any(fragment => password.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
