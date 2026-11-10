using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;

namespace TutorManagement.Modules.Finance.Application.Payments;

/// <summary>A QR code of this center, as needed to match a transfer to it.</summary>
public sealed record IntentRef(Guid Id, string Code, Guid StudentId, string AccountNumber);

/// <summary>What the matcher knows about one center.</summary>
public sealed record MatchContext(
    string? ConfigAccount,
    IReadOnlyCollection<string> IntentAccounts,
    IReadOnlyDictionary<string, IntentRef> IntentsByCode,
    IReadOnlyList<TuitionStudentRow> Students);

public enum MatchKind
{
    Book = 1,
    Review = 2,
    Ignore = 3
}

/// <summary>
/// The matcher's answer for one transfer. <see cref="Via"/> says how the student was found
/// ("PaymentCode HP…", "StudentCode HS…") or why not.
/// </summary>
public sealed record MatchDecision(MatchKind Kind, Guid? StudentId, Guid? IntentId, ReviewReason? Review, IgnoreReason? Ignore,
    string Via)
{
    public static MatchDecision Booked(Guid studentId, Guid? intentId, string via) => new(MatchKind.Book, studentId, intentId, null, null, via);
    public static MatchDecision NeedsReview(ReviewReason reason, string via) => new(MatchKind.Review, null, null, reason, null, via);
    public static MatchDecision Ignored(IgnoreReason reason, string via) => new(MatchKind.Ignore, null, null, null, reason, via);

    /// <summary>Stored in <c>shadow_outcome</c>.</summary>
    public string Outcome => Kind switch
    {
        MatchKind.Book => "Book",
        MatchKind.Review => $"Review:{Review}",
        _ => $"Ignore:{Ignore}"
    };
}

/// <summary>
/// Matching rules of docs/THANH-TOAN-QR-KE-HOACH.md, section 3 ("Quy tắc khớp"). Never guesses by name or amount:
/// a transfer is booked only through a QR code of this center or a student code that fits exactly one student.
/// </summary>
public static class PaymentMatcher
{
    /// <summary>Receipts cannot hold more than this (<see cref="Receipt.Create"/>).</summary>
    public const long MaxAmount = 10 * TuitionMath.MaxFee;

    public static MatchDecision Match(BankTransaction transaction, MatchContext context)
    {
        // 2. Money into an account the center does not use for tuition is not tuition.
        var accounts = context.IntentAccounts.Append(context.ConfigAccount).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Normalize).ToHashSet(StringComparer.Ordinal);
        if (!accounts.Contains(Normalize(transaction.AccountNumber)) &&
            !(transaction.VirtualAccount is { } va && accounts.Contains(Normalize(va))))
            return MatchDecision.Ignored(IgnoreReason.OtherAccount, $"Account {transaction.AccountNumber} is not this center's");

        // 3. A receipt cannot hold it.
        if (transaction.Amount > MaxAmount) return MatchDecision.NeedsReview(ReviewReason.TooLarge, $"Amount {transaction.Amount}");

        // 4–5. QR codes of this center.
        var codes = PaymentCode.FindIn(transaction.Content);
        var known = codes.Where(context.IntentsByCode.ContainsKey).Select(x => context.IntentsByCode[x]).ToArray();
        if (known.Length > 1)
            return MatchDecision.NeedsReview(ReviewReason.AmbiguousCode, "Codes " + string.Join(", ", known.Select(x => x.Code)));
        var students = context.Students.ToDictionary(x => x.Id);
        if (known.Length == 1)
            return MatchDecision.Booked(Resolve(students, known[0].StudentId), known[0].Id, $"PaymentCode {known[0].Code}");

        // 6. The older content "HS000123 HOC PHI": the student code must fit exactly one profile.
        var words = Words(transaction.Content);
        var byCode = context.Students
            .Where(x => StudentCode(x.Code) is { Length: >= 4 } code && words.Contains(code))
            .Select(x => Resolve(students, x.Id)).Distinct().ToArray();
        if (byCode.Length > 1) return MatchDecision.NeedsReview(ReviewReason.AmbiguousCode, "Several student codes");
        if (byCode.Length == 1)
            return MatchDecision.Booked(byCode[0], null, $"StudentCode {StudentCode(students[byCode[0]].Code)}");

        // 7. Nothing to go on.
        return codes.Count > 0
            ? MatchDecision.NeedsReview(ReviewReason.UnknownCode, "Code " + codes[0] + " is not a QR code of this center")
            : MatchDecision.NeedsReview(ReviewReason.NoCode, "No code in the content");
    }

    /// <summary>The code part of <see cref="TuitionViews.TransferContent"/>: ASCII letters and digits, upper case, at most 16.</summary>
    public static string StudentCode(string code)
    {
        var text = new string(code.Normalize(System.Text.NormalizationForm.FormD).Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant).ToArray());
        return text.Length > 16 ? text[..16] : text;
    }

    private static Guid Resolve(Dictionary<Guid, TuitionStudentRow> students, Guid studentId)
    {
        var current = studentId;
        for (var hop = 0; hop < 10 && students.TryGetValue(current, out var row) && row.MergedIntoStudentId is { } next; hop++)
            current = next;
        return current;
    }

    private static HashSet<string> Words(string content) =>
        content.ToUpperInvariant().Split(static x => !char.IsAsciiLetterOrDigit(x)).Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static string Normalize(string? value) =>
        string.Concat((value ?? string.Empty).Where(x => !char.IsWhiteSpace(x))).ToUpperInvariant();
}

internal static class SplitExtensions
{
    public static IEnumerable<string> Split(this string value, Func<char, bool> separator)
    {
        var start = 0;
        for (var at = 0; at <= value.Length; at++)
        {
            if (at < value.Length && !separator(value[at])) continue;
            yield return value[start..at];
            start = at + 1;
        }
    }
}
