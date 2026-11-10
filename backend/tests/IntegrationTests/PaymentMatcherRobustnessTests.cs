using System.Security.Cryptography;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using Xunit.Abstractions;

namespace TutorManagement.IntegrationTests;

/// <summary>
/// The matcher against the content people and banks actually produce: bank prefixes and suffixes, cut or glued text,
/// mistyped codes, random noise. A transfer must never be booked to the wrong student.
/// </summary>
public sealed class PaymentMatcherRobustnessTests(ITestOutputHelper output)
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const string Account = "0011223344";
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);

    private static readonly PaymentProviderConfig Config = PaymentProviderConfig.Create(Guid.NewGuid(), PaymentProvider.Sepay,
        "970436", Account, PaymentIngestionDomainTests.Secrets(
            PaymentIngestionDomainTests.Protector("k", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ("k", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))), Guid.NewGuid(), "t"), Guid.NewGuid(), Now);

    private static BankTransaction Transfer(string content) =>
        BankTransaction.Receive(Config, new ReceivedBankTransaction(Guid.NewGuid().ToString(), "Vietcombank", Account, null, null,
            TransferDirection.In, 800_000, content, null, Now, "{}"), FetchedBy.Doorbell, Now);

    private static readonly TuitionStudentRow An = new(Guid.NewGuid(), "HS000123", "Nguyễn An", null, null, null, null, false);
    private static readonly TuitionStudentRow Binh = new(Guid.NewGuid(), "HS000124", "Lê Bình", null, null, null, null, false);
    private static readonly TuitionStudentRow Chi = new(Guid.NewGuid(), "HS000012", "Trần Chi", null, null, null, null, false);

    // ---------------------------------------------------------------- 1. content as banks write it

    public static TheoryData<string, string> BankContents()
    {
        // {0} is An's QR code. The expected result is "An", "Binh", "Chi" or a review reason.
        var data = new TheoryData<string, string>
        {
            { "{0} HOC PHI", "An" },
            { "{0}", "An" },
            { "{0}HOCPHI", "An" },                                           // bank dropped the spaces
            { "MBVCB.3278561529.{0} HOC PHI.CT tu 0071000123456 NGUYEN VAN A toi 0011223344", "An" },
            { "CUSTOMER {0} HOC PHI. TU: NGUYEN VAN A", "An" },
            { "{0} HOC PHI FT24315012345678", "An" },
            { "GD 123456-101126 10:15:22 {0} HOC PHI", "An" },
            { "IBFT {0} HOC PHI", "An" },
            { "TKThe :0071000123456, tai Vietcombank. {0} HOC PHI -CTLNHIDO000012345", "An" },
            { "{0} học phí tháng 11 bé An", "An" },                          // typed with Vietnamese accents
            { "chuyen tien {0:lower} hoc phi", "An" },                       // lower case
            { "{0:spaced} HOC PHI", "An" },                                  // a space inside the code
            { "{0:dotted}", "An" },                                          // dots inside the code
            { "{0:lookalike} HOC PHI", "An" },                               // O for 0, I or L for 1
            { "Thanh toan{0}hoc phi", "An" },                                // glued to the words around it
            { "{0} {0} HOC PHI", "An" },                                     // the same code twice
            { "HS000124 HOC PHI", "Binh" },                                  // the older student-code content
            { "hs000124 hoc phi", "Binh" },
            { "NGUYEN VAN A CHUYEN TIEN HS000124", "Binh" },
            { "HS000012 HOC PHI", "Chi" },                                   // not confused with HS000123/HS000124
            { "{0:damaged} HOC PHI", "UnknownCode" },                        // one character mistyped
            { "{0:cut} HOC PHI", "UnknownCode" },                            // bank cut the code short: a code was typed
            { "HOC PHI BE AN LOP 9A", "NoCode" },                            // a name is never enough
            { "NGUYEN AN 800000", "NoCode" },
            { "HS 000124 HOC PHI", "NoCode" },                               // student code split by a space: not guessed
            { "XHS000124 HOC PHI", "NoCode" },                               // part of a longer word
            { "HS000123 HS000124", "AmbiguousCode" },                        // two children in one transfer
            { "{0} {B} HOC PHI", "AmbiguousCode" },                          // two QR codes
            { "", "NoCode" },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(BankContents))]
    public void Bank_content_is_read_as_people_and_banks_write_it(string template, string expected)
    {
        var codeA = PaymentCode.New();
        var codeB = PaymentCode.New();
        var content = template
            .Replace("{0:lower}", codeA.ToLowerInvariant())
            .Replace("{0:spaced}", codeA[..5] + " " + codeA[5..])
            .Replace("{0:dotted}", codeA[..2] + "." + codeA[2..6] + "." + codeA[6..])
            .Replace("{0:lookalike}", codeA.Replace('0', 'O').Replace('1', 'I'))
            .Replace("{0:damaged}", Damaged(codeA))
            .Replace("{0:cut}", codeA[..^2])
            .Replace("{0}", codeA)
            .Replace("{B}", codeB);
        var context = new MatchContext(Account, [], new Dictionary<string, IntentRef>
        {
            [codeA] = new(Guid.NewGuid(), codeA, An.Id, Account),
            [codeB] = new(Guid.NewGuid(), codeB, Binh.Id, Account)
        }, [An, Binh, Chi]);

        var decision = PaymentMatcher.Match(Transfer(content), context);
        var actual = decision.Kind == MatchKind.Book
            ? decision.StudentId == An.Id ? "An" : decision.StudentId == Binh.Id ? "Binh" : "Chi"
            : decision.Review?.ToString() ?? decision.Ignore.ToString()!;
        Assert.True(expected == actual, $"\"{content}\" → {actual} ({decision.Via}), expected {expected}");
    }

    private static string Damaged(string code)
    {
        var at = 5;
        var replacement = Alphabet[(Alphabet.IndexOf(code[at]) + 7) % Alphabet.Length];
        return code[..at] + replacement + code[(at + 1)..];
    }

    // ---------------------------------------------------------------- 2. mistyped codes

    [Fact]
    public void Every_single_mistyped_character_is_caught()
    {
        var caught = 0;
        var total = 0;
        for (var round = 0; round < 300; round++)
        {
            var code = PaymentCode.New();
            for (var at = 2; at < code.Length; at++)
                foreach (var other in Alphabet.Where(x => x != code[at]))
                {
                    total++;
                    if (!PaymentCode.IsValid(code[..at] + other + code[(at + 1)..])) caught++;
                }
        }
        output.WriteLine($"one wrong character: {caught}/{total} caught");
        Assert.Equal(total, caught);
    }

    [Fact]
    public void Swapped_neighbours_never_book_to_another_student()
    {
        // The parent's QR code (An) among 2,000 live QR codes of other students (Binh).
        var others = Enumerable.Range(0, 2000).Select(_ => PaymentCode.New()).ToHashSet();
        int swaps = 0, caught = 0, toSomeoneElse = 0;
        for (var round = 0; round < 3000; round++)
        {
            var code = PaymentCode.New();
            var intents = others.Where(x => x != code)
                .ToDictionary(x => x, x => new IntentRef(Guid.NewGuid(), x, Binh.Id, Account));
            intents[code] = new IntentRef(Guid.NewGuid(), code, An.Id, Account);
            var context = new MatchContext(Account, [], intents, [An, Binh, Chi]);
            for (var at = 2; at < code.Length - 1; at++)
            {
                if (code[at] == code[at + 1]) continue;
                swaps++;
                var swapped = code[..at] + code[at + 1] + code[at] + code[(at + 2)..];
                if (!PaymentCode.IsValid(swapped)) caught++;
                var decision = PaymentMatcher.Match(Transfer(swapped + " HOC PHI"), context);
                if (decision.Kind == MatchKind.Book && decision.StudentId != An.Id) toSomeoneElse++;
            }
        }
        var rate = (double)caught / swaps;
        output.WriteLine($"two neighbours swapped: {caught}/{swaps} caught ({rate:P2}); booked to someone else {toSomeoneElse}");
        // Luhn mod 32 misses a few character pairs; such a code still has to be a live QR code of this center to book.
        Assert.True(rate > 0.95, $"only {rate:P2} of swaps caught");
        Assert.Equal(0, toSomeoneElse);
    }

    [Fact]
    public void A_dropped_character_never_books_to_another_student()
    {
        // A busy center: the parent's QR code (An) and 2,000 live QR codes of other students (Binh).
        var others = Enumerable.Range(0, 2000).Select(_ => PaymentCode.New()).ToHashSet();
        int checkedCount = 0, repaired = 0, otherFormat = 0, toAn = 0, toSomeoneElse = 0, review = 0;
        for (var round = 0; round < 5000; round++)
        {
            var code = PaymentCode.New();
            var intents = others.Where(x => x != code)
                .ToDictionary(x => x, x => new IntentRef(Guid.NewGuid(), x, Binh.Id, Account));
            intents[code] = new IntentRef(Guid.NewGuid(), code, An.Id, Account);
            var context = new MatchContext(Account, [], intents, [An, Binh, Chi]);
            for (var at = 2; at < code.Length; at++)
            {
                checkedCount++;
                var content = code.Remove(at, 1) + " HOC PHI";
                var found = PaymentCode.FindIn(content);
                // The next letters ("HOC") can complete a code cut at its end: still the parent's own code.
                if (found.Contains(code)) repaired++;
                otherFormat += found.Count(x => x != code);
                var decision = PaymentMatcher.Match(Transfer(content), context);
                if (decision.Kind != MatchKind.Book) review++;
                else if (decision.StudentId == An.Id) toAn++;
                else toSomeoneElse++;
            }
        }
        output.WriteLine($"one character dropped: {checkedCount} cases; {repaired} still read as the parent's own code; " +
                         $"{otherFormat} happened to look like some other code; booked to the parent's child {toAn}, " +
                         $"to someone else {toSomeoneElse}, sent to review {review}");
        Assert.Equal(0, toSomeoneElse);
    }

    // ---------------------------------------------------------------- 3. random noise

    [Fact]
    public void Random_content_never_books_to_a_qr_code_of_the_center()
    {
        // A busy center: 2,000 live QR codes. 200,000 transfers whose content is random letters, digits and words.
        var intents = Enumerable.Range(0, 2000).Select(_ => PaymentCode.New()).Distinct()
            .ToDictionary(x => x, x => new IntentRef(Guid.NewGuid(), x, An.Id, Account));
        var context = new MatchContext(Account, [], intents, [An, Binh, Chi]);
        var random = new Random(20261110);
        const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .-:/";
        int formatHits = 0, booked = 0;
        for (var i = 0; i < 200_000; i++)
        {
            var length = random.Next(5, 120);
            var chars = new char[length];
            for (var k = 0; k < length; k++) chars[k] = Letters[random.Next(Letters.Length)];
            // Make "HP" common so the scanner is really exercised.
            if (length > 15) { var at = random.Next(length - 13); chars[at] = 'H'; chars[at + 1] = 'P'; }
            var content = new string(chars);
            if (PaymentCode.FindIn(content).Count > 0) formatHits++;
            if (PaymentMatcher.Match(Transfer(content), context).Kind == MatchKind.Book) booked++;
        }
        output.WriteLine($"random content: {formatHits} looked like some code, {booked} booked to a QR code of the center");
        Assert.Equal(0, booked);
    }

    // ---------------------------------------------------------------- 4. codes hidden in messy content

    [Fact]
    public void A_code_inside_messy_content_is_always_found()
    {
        var random = new Random(42);
        string Noise(int max)
        {
            const string Words = "CHUYEN TIEN HOC PHI THANG BE CON LOP NGUYEN VAN THI TRAN LE PHAM FT MBVCB IBFT GD TU TOI";
            var parts = Words.Split(' ');
            return string.Join(' ', Enumerable.Range(0, random.Next(0, max)).Select(_ => parts[random.Next(parts.Length)]));
        }
        string Disguise(string code) => random.Next(4) switch
        {
            0 => code.ToLowerInvariant(),
            1 => code.Replace('0', 'O').Replace('1', 'L'),
            2 => code[..random.Next(3, 9)] + " " + code[random.Next(3, 9)..] is var _ ? code[..6] + " " + code[6..] : code,
            _ => code
        };
        var missed = new List<string>();
        for (var i = 0; i < 20_000; i++)
        {
            var code = PaymentCode.New();
            var separator = random.Next(3) switch { 0 => " ", 1 => ".", _ => "" };
            var content = $"{Noise(6)}{separator}{Disguise(code)}{separator}{Noise(6)}";
            if (!PaymentCode.FindIn(content).Contains(code)) missed.Add(content);
        }
        output.WriteLine($"codes hidden in noise: {20_000 - missed.Count}/20000 found");
        Assert.True(missed.Count == 0, "missed: " + string.Join(" | ", missed.Take(5)));
    }
}
