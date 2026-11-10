using System.Security.Cryptography;

namespace TutorManagement.Modules.Finance.Domain.Tuition;

/// <summary>
/// The code a parent's transfer carries: "HP", 8 random Crockford base32 characters and a Luhn mod 32 check character.
/// The check catches a mistyped character, so a damaged code is not matched to someone else's payment.
/// </summary>
public static class PaymentCode
{
    public const int Length = 11;
    private const string Prefix = "HP";
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int RandomLength = 8;

    public static string New()
    {
        var body = RandomNumberGenerator.GetString(Alphabet, RandomLength);
        return Prefix + body + CheckCharacter(body);
    }

    public static bool IsValid(string? code)
    {
        if (code is null || code.Length != Length || !code.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var body = code[Prefix.Length..^1];
        return body.All(x => Alphabet.Contains(x)) && code[^1] == CheckCharacter(body);
    }

    /// <summary>
    /// Every valid code inside a bank transfer's content, in order of appearance. Banks may drop spaces
    /// ("HPFGXXVAPYPHOCPHI") or change case, and people mistype look-alikes, so the content is read as one run of
    /// letters and digits and O/I/L are read as 0/1/1 after "HP". The check character keeps a damaged code from
    /// matching someone else's payment.
    /// </summary>
    public static IReadOnlyList<string> FindIn(string? content)
    {
        if (string.IsNullOrEmpty(content)) return [];
        var run = new string(content.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        var found = new List<string>();
        for (var at = run.IndexOf(Prefix, StringComparison.Ordinal); at >= 0 && at + Length <= run.Length;
             at = run.IndexOf(Prefix, at + 1, StringComparison.Ordinal))
        {
            var candidate = Prefix + string.Concat(run.AsSpan(at + Prefix.Length, Length - Prefix.Length).ToArray()
                .Select(x => x switch { 'O' => '0', 'I' or 'L' => '1', _ => x }));
            if (IsValid(candidate) && !found.Contains(candidate)) found.Add(candidate);
        }
        return found;
    }

    private static char CheckCharacter(string body)
    {
        var sum = 0;
        var factor = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            var addend = factor * Alphabet.IndexOf(body[i]);
            sum += addend / Alphabet.Length + addend % Alphabet.Length;
            factor = factor == 2 ? 1 : 2;
        }
        return Alphabet[(Alphabet.Length - sum % Alphabet.Length) % Alphabet.Length];
    }
}
