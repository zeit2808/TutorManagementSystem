using System.Text.RegularExpressions;

namespace TutorManagement.Modules.Academics.Domain.Teaching;

/// <summary>Decision D5: a student's record carries a parent name and Vietnamese mobile number.</summary>
public static partial class ParentContact
{
    public static bool IsValidName(string? value) => TeachingClass.IsValidName(value);

    /// <summary>Accepts 0912 345 678, 84912345678 or +84912345678 and returns +84912345678.</summary>
    public static bool TryNormalizePhone(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var compact = Formatting().Replace(value, string.Empty);
        if (compact.StartsWith('0')) compact = $"+84{compact[1..]}";
        else if (compact.StartsWith("84", StringComparison.Ordinal)) compact = $"+{compact}";
        if (!VietnamMobile().IsMatch(compact)) return false;
        normalized = compact;
        return true;
    }

    [GeneratedRegex(@"[\s.()\-]")]
    private static partial Regex Formatting();

    [GeneratedRegex(@"^\+84(?:3|5|7|8|9)\d{8}$")]
    private static partial Regex VietnamMobile();
}
