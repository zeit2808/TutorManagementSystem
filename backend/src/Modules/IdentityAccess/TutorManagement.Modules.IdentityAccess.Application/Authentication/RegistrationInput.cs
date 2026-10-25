using System.Text.RegularExpressions;

namespace TutorManagement.Modules.IdentityAccess.Application.Authentication;

public static partial class RegistrationInput
{
    public static string NormalizeUsername(string username) =>
        username.Trim().ToUpperInvariant();

    public static string NormalizeEmail(string email) =>
        email.Trim().ToUpperInvariant();

    public static bool TryNormalizeVietnamesePhoneNumber(
        string? phoneNumber,
        out string normalizedPhoneNumber)
    {
        normalizedPhoneNumber = string.Empty;
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return false;
        }

        var compact = PhoneFormattingCharacters().Replace(phoneNumber, string.Empty);
        if (compact.StartsWith('0'))
        {
            compact = $"+84{compact[1..]}";
        }
        else if (compact.StartsWith("84", StringComparison.Ordinal))
        {
            compact = $"+{compact}";
        }

        if (!VietnameseMobileNumber().IsMatch(compact))
        {
            return false;
        }

        normalizedPhoneNumber = compact;
        return true;
    }

    [GeneratedRegex(@"[\s.()\-]")]
    private static partial Regex PhoneFormattingCharacters();

    [GeneratedRegex(@"^\+84(?:3|5|7|8|9)\d{8}$")]
    private static partial Regex VietnameseMobileNumber();
}
