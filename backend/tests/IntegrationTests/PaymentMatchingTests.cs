using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 3a on pure values: reading the code out of a transfer and the matching rules.</summary>
public sealed class PaymentMatchingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 5, 0, 0, TimeSpan.Zero);
    private const string Account = "0011223344";

    private static readonly PaymentProviderConfig Config = PaymentProviderConfig.Create(Guid.NewGuid(), PaymentProvider.Sepay,
        "970436", Account, PaymentIngestionDomainTests.Secrets(
            PaymentIngestionDomainTests.Protector("k", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                ("k", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))), Guid.NewGuid(), "t"), Guid.NewGuid(), Now);

    private static BankTransaction Transfer(string content, string account = Account, string? va = null, long amount = 800_000) =>
        BankTransaction.Receive(Config, new ReceivedBankTransaction(Guid.NewGuid().ToString(), "Vietcombank", account, null, va,
            TransferDirection.In, amount, content, null, Now, "{}"), FetchedBy.Doorbell, Now);

    private static readonly TuitionStudentRow An = new(Guid.NewGuid(), "HS000123", "Nguyễn An", null, null, null, null, false);
    private static readonly TuitionStudentRow Binh = new(Guid.NewGuid(), "HS000124", "Lê Bình", null, null, null, null, false);
    private static readonly TuitionStudentRow OldAn = new(Guid.NewGuid(), "HS000099", "Nguyễn An (cũ)", null, An.Id, null, null, false);

    private static MatchContext Context(params IntentRef[] intents) =>
        new(Account, ["9999000011"], intents.ToDictionary(x => x.Code), [An, Binh, OldAn]);

    [Fact]
    public void Codes_are_found_without_spaces_in_any_case_and_with_look_alikes_but_not_when_damaged()
    {
        var code = PaymentCode.New();
        Assert.Equal([code], PaymentCode.FindIn($"{code} HOC PHI"));
        Assert.Equal([code], PaymentCode.FindIn($"{code}HOCPHI"));
        Assert.Equal([code], PaymentCode.FindIn($"chuyen tien {code.ToLowerInvariant()} hoc phi"));
        Assert.Equal([code], PaymentCode.FindIn($"{code[..5]} {code[5..]}"));
        var withLookAlikes = code.Replace('0', 'O').Replace('1', 'I');
        Assert.Equal([code], PaymentCode.FindIn(withLookAlikes));
        var damaged = code[..^1] + (code[^1] == 'A' ? 'B' : 'A');
        Assert.Empty(PaymentCode.FindIn(damaged));
        Assert.Empty(PaymentCode.FindIn(null));
        var other = PaymentCode.New();
        Assert.Equal([code, other], PaymentCode.FindIn($"{code} {other} {code}"));
    }

    [Fact]
    public void A_qr_code_of_the_center_books_to_its_student_even_after_a_merge()
    {
        var code = PaymentCode.New();
        var intent = new IntentRef(Guid.NewGuid(), code, OldAn.Id, Account);
        var decision = PaymentMatcher.Match(Transfer($"{code} HOC PHI"), Context(intent));
        Assert.Equal(MatchKind.Book, decision.Kind);
        Assert.Equal(An.Id, decision.StudentId);
        Assert.Equal(intent.Id, decision.IntentId);
        Assert.Equal("Book", decision.Outcome);
        Assert.Equal($"PaymentCode {code}", decision.Via);
    }

    [Fact]
    public void Accounts_count_through_the_virtual_account_and_older_qr_accounts_otherwise_it_is_not_tuition()
    {
        var code = PaymentCode.New();
        var context = Context(new IntentRef(Guid.NewGuid(), code, An.Id, Account));
        Assert.Equal(MatchKind.Book, PaymentMatcher.Match(Transfer($"{code}", account: "0000000001", va: Account), context).Kind);
        Assert.Equal(MatchKind.Book, PaymentMatcher.Match(Transfer($"{code}", account: "9999000011"), context).Kind);
        var other = PaymentMatcher.Match(Transfer($"{code}", account: "5555"), context);
        Assert.Equal((MatchKind.Ignore, IgnoreReason.OtherAccount), (other.Kind, other.Ignore));
        Assert.Equal("Ignore:OtherAccount", other.Outcome);
    }

    [Fact]
    public void Two_codes_unknown_codes_huge_amounts_and_no_code_go_to_review()
    {
        var (a, b) = (PaymentCode.New(), PaymentCode.New());
        var context = Context(new IntentRef(Guid.NewGuid(), a, An.Id, Account), new IntentRef(Guid.NewGuid(), b, Binh.Id, Account));
        Assert.Equal(ReviewReason.AmbiguousCode, PaymentMatcher.Match(Transfer($"{a} {b} HOC PHI"), context).Review);
        Assert.Equal(ReviewReason.UnknownCode, PaymentMatcher.Match(Transfer($"{PaymentCode.New()} HOC PHI"), context).Review);
        Assert.Equal(ReviewReason.TooLarge, PaymentMatcher.Match(Transfer($"{a}", amount: PaymentMatcher.MaxAmount + 1), context).Review);
        var none = PaymentMatcher.Match(Transfer("Chuyen tien hoc thang 11"), context);
        Assert.Equal((MatchKind.Review, ReviewReason.NoCode, "Review:NoCode"), (none.Kind, none.Review, none.Outcome));
        Assert.Null(none.StudentId);
    }

    [Fact]
    public void The_older_student_code_content_books_only_when_it_fits_one_profile()
    {
        var context = Context();
        var binh = PaymentMatcher.Match(Transfer("HS000124 HOC PHI"), context);
        Assert.Equal((MatchKind.Book, Binh.Id, (Guid?)null), (binh.Kind, binh.StudentId, binh.IntentId));
        Assert.Equal("StudentCode HS000124", binh.Via);
        Assert.Equal(An.Id, PaymentMatcher.Match(Transfer("hs000099 hoc phi"), context).StudentId);
        // A code inside a longer word is not that student.
        Assert.Equal(ReviewReason.NoCode, PaymentMatcher.Match(Transfer("XHS000124 HOC PHI"), context).Review);
        Assert.Equal(ReviewReason.AmbiguousCode, PaymentMatcher.Match(Transfer("HS000123 HS000124"), context).Review);
        // A merged profile and the one it went into are the same student.
        Assert.Equal(An.Id, PaymentMatcher.Match(Transfer("HS000123 HS000099"), context).StudentId);
    }
}

/// <summary>Round 3a against real PostgreSQL: Shadow centers get the expected result written next to each transfer.</summary>
[Collection(TuitionDatabase.Name)]
public sealed class RealPostgresShadowBookingTests
{
    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [LocalPostgresFact]
    public async Task Shadow_records_what_would_be_booked_and_creates_no_receipt()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = new DateTimeOffset(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
        var clock = new Clock(now);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var center = Center.Create("Shadow LLC", $"Shadow {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"shadow-{marker}@example.invalid", "0900000000", now);
        center.Approve(Guid.NewGuid(), now);
        var an = Student.Create(center.Id, $"HS{marker[..6].ToUpperInvariant()}", "Nguyễn An", now);
        var binh = Student.Create(center.Id, $"HB{marker[..6].ToUpperInvariant()}", "Lê Bình", now);
        await using var data = new PaymentTestData(connectionString);
        data.Center(center.Id);
        var offCenter = data.Center(Guid.NewGuid());

        await using (var organization = new OrganizationDbContext(
                         new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options))
        {
            organization.Centers.Add(center);
            await organization.SaveChangesAsync();
        }
        await using (var academics = new AcademicsDbContext(
                         new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options))
        {
            academics.Students.AddRange(an, binh);
            await academics.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var settings = FinanceSettings.Default(center.Id);
        settings.Update("970436", account, "TRUNG TAM SHADOW", 7, true, Guid.NewGuid(), now);
        var config = PaymentProviderConfig.Create(center.Id, PaymentProvider.Sepay, "970436", account,
            PaymentIngestionDomainTests.Secrets(protector, center.Id, $"tok-{marker}"), Guid.NewGuid(), now);
        config.ChangeMode(PaymentProviderMode.Shadow, Guid.NewGuid(), now);
        var off = PaymentProviderConfig.Create(offCenter, PaymentProvider.Sepay, "970436", account + "7",
            PaymentIngestionDomainTests.Secrets(protector, offCenter, $"off-{marker}"), Guid.NewGuid(), now);
        var intent = PaymentIntent.Create(center.Id, an.Id, PaymentCode.New(), 800_000, [Guid.NewGuid()], settings, Guid.NewGuid(), now);
        ReceivedBankTransaction Incoming(string content, string to = "") => new($"{marker}-{Guid.NewGuid():N}", "Vietcombank",
            to.Length == 0 ? account : to, null, null, TransferDirection.In, 800_000, content, $"SB{Guid.NewGuid():N}"[..14], now, "{}");
        var byQr = BankTransaction.Receive(config, Incoming($"{intent.Code} HOC PHI"), FetchedBy.Doorbell, now);
        var byStudent = BankTransaction.Receive(config, Incoming($"{binh.Code} HOC PHI"), FetchedBy.Doorbell, now);
        var elsewhere = BankTransaction.Receive(config, Incoming($"{intent.Code}", "123123123"), FetchedBy.Doorbell, now);
        var noCode = BankTransaction.Receive(config, Incoming("chuyen tien"), FetchedBy.Doorbell, now);
        var held = BankTransaction.HoldAsDuplicate(config, Incoming($"{intent.Code}"), byQr.Id, FetchedBy.Nightly, now);
        var ofOff = BankTransaction.Receive(off, Incoming($"{intent.Code}", account + "7"), FetchedBy.Doorbell, now);
        await using (var db = new FinanceDbContext(options))
        {
            db.FinanceSettings.Add(settings);
            db.PaymentProviderConfigs.AddRange(config, off);
            db.PaymentIntents.Add(intent);
            db.BankTransactions.AddRange(byQr, byStudent, elsewhere, noCode, held, ofOff);
            await db.SaveChangesAsync();
        }

        async Task Run()
        {
            await using var db = new FinanceDbContext(options);
            var academicsOptions = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
            await using var reader = new FinanceDbContext(academicsOptions);
            await new PaymentBooker(new PaymentBookingStore(db), new TuitionAcademicsReader(reader), clock).RunShadowOnceAsync(500, default);
        }
        async Task<Dictionary<Guid, BankTransaction>> Rows()
        {
            await using var db = new FinanceDbContext(options);
            return await db.BankTransactions.AsNoTracking()
                .Where(x => x.CenterId == center.Id || x.CenterId == offCenter).ToDictionaryAsync(x => x.Id);
        }

        await Run();
        var rows = await Rows();
        Assert.Equal(("Book", intent.Id, an.Id), (rows[byQr.Id].ShadowOutcome, rows[byQr.Id].ShadowIntentId, rows[byQr.Id].ShadowStudentId));
        Assert.Equal($"PaymentCode {intent.Code}", rows[byQr.Id].ShadowReason);
        Assert.Equal(("Book", (Guid?)null, binh.Id), (rows[byStudent.Id].ShadowOutcome, rows[byStudent.Id].ShadowIntentId, rows[byStudent.Id].ShadowStudentId));
        Assert.Equal("Ignore:OtherAccount", rows[elsewhere.Id].ShadowOutcome);
        Assert.Equal("Review:NoCode", rows[noCode.Id].ShadowOutcome);
        Assert.All(new[] { byQr, byStudent, elsewhere, noCode }, x =>
        {
            Assert.Equal(BankTransactionStatus.Received, rows[x.Id].Status);
            Assert.Equal(now, rows[x.Id].ShadowEvaluatedAtUtc);
            Assert.Null(rows[x.Id].ProcessingId);
        });
        Assert.Null(rows[held.Id].ShadowEvaluatedAtUtc);
        Assert.Null(rows[ofOff.Id].ShadowEvaluatedAtUtc);

        // Evaluated once: a second pass leaves the results alone.
        await Run();
        Assert.Equal(rows[byQr.Id].ShadowEvaluatedAtUtc, (await Rows())[byQr.Id].ShadowEvaluatedAtUtc);
        await using (var db = new FinanceDbContext(options))
            Assert.False(await db.Receipts.AnyAsync(x => x.CenterId == center.Id));
    }
}
