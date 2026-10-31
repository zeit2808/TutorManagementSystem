using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>The transfer code and the VietQR text on pure values.</summary>
public sealed class PaymentQrTests
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>();
        for (var at = 0; at < text.Length;)
        {
            var length = int.Parse(text.AsSpan(at + 2, 2), CultureInfo.InvariantCulture);
            fields[text.Substring(at, 2)] = text.Substring(at + 4, length);
            at += 4 + length;
        }
        return fields;
    }

    [Fact]
    public void Codes_are_well_formed_and_a_mistyped_character_is_caught()
    {
        var code = PaymentCode.New();
        Assert.Equal(PaymentCode.Length, code.Length);
        Assert.StartsWith("HP", code, StringComparison.Ordinal);
        Assert.True(PaymentCode.IsValid(code));
        for (var at = 2; at < code.Length; at++)
            foreach (var other in Alphabet.Where(x => x != code[at]))
                Assert.False(PaymentCode.IsValid(code[..at] + other + code[(at + 1)..]));
        Assert.False(PaymentCode.IsValid(code[..^1]));
        Assert.False(PaymentCode.IsValid("HS" + code[2..]));
        Assert.False(PaymentCode.IsValid(code.ToLowerInvariant()));
        Assert.False(PaymentCode.IsValid(null));
    }

    [Fact]
    public void The_checksum_is_crc16_ccitt_false() => Assert.Equal(0x29B1, VietQr.Crc16("123456789"));

    [Fact]
    public void The_qr_text_carries_the_account_amount_and_content()
    {
        var text = VietQr.Build("970436", "0123456789", 2_400_000, "HP12345678X HOC PHI");
        var fields = Fields(text);
        Assert.Equal("01", fields["00"]);
        Assert.Equal("12", fields["01"]);
        Assert.Equal("704", fields["53"]);
        Assert.Equal("2400000", fields["54"]);
        Assert.Equal("VN", fields["58"]);
        Assert.Equal("HP12345678X HOC PHI", Fields(fields["62"])["08"]);
        var merchant = Fields(fields["38"]);
        Assert.Equal("A000000727", merchant["00"]);
        Assert.Equal("QRIBFTTA", merchant["02"]);
        var beneficiary = Fields(merchant["01"]);
        Assert.Equal("970436", beneficiary["00"]);
        Assert.Equal("0123456789", beneficiary["01"]);
        Assert.Equal(VietQr.Crc16(text[..^4]).ToString("X4", CultureInfo.InvariantCulture), fields["63"]);
    }

    [Fact]
    public void A_content_the_code_cannot_carry_is_refused()
    {
        Assert.Throws<ArgumentException>(() => VietQr.Build("970436", "0123456789", 0, "HOC PHI"));
        Assert.Throws<ArgumentException>(() => VietQr.Build("970436", "0123456789", 1000, "HỌC PHÍ"));
        Assert.Throws<ArgumentException>(() => VietQr.Build("970436", "0123456789", 1000, new string('A', 26)));
    }
}

/// <summary>Making the QR code against real PostgreSQL: the amount follows the ledger and repeats reuse the code.</summary>
public sealed class RealPostgresPaymentIntentTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static DateTimeOffset Local(int month, int day, int hour = 18) =>
        new DateTimeOffset(2026, month, day, hour, 0, 0, Vietnam.GetUtcOffset(new DateTime(2026, month, day))).ToUniversalTime();

    [LocalPostgresFact]
    public async Task The_qr_code_follows_the_ledger_and_is_reused_until_the_amount_changes()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var clock = new MutableClock(Local(3, 20, 9));
        var past = Local(1, 1, 8);
        var marker = Guid.NewGuid().ToString("N")[..8];
        var owner = User.CreateExternal($"owner-{marker}@example.invalid", past);
        var anUser = User.CreateExternal($"an-{marker}@example.invalid", past);
        var strangerUser = User.CreateExternal($"stranger-{marker}@example.invalid", past);
        var center = Center.Create("QR LLC", $"QR {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"qr-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner.Id, past);
        var room = Room.Create(center.Id, "P1", "Phòng 1", null, owner.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var orgOptions = new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options;
        var options = new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();

        try
        {
            await using (var identity = new IdentityDbContext(identityOptions))
            {
                identity.Users.AddRange(owner, anUser, strangerUser);
                await identity.SaveChangesAsync();
            }
            await using (var organization = new OrganizationDbContext(orgOptions))
            {
                organization.Centers.Add(center);
                organization.Rooms.Add(room);
                await organization.SaveChangesAsync();
            }
            var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, owner.Id, past);
            var teacher = Teacher.Create(center.Id, "GV01", "Cô Lan", past);
            var math = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 20, owner.Id, past);
            var an = Student.CreateForUser(center.Id, anUser.Id, "HS-000001", "Nguyễn An", past);
            var stranger = Student.CreateForUser(center.Id, strangerUser.Id, "HS-000002", "Lê Bình", past);
            // Every Thursday from 15/01: on 20/03 cycles 15/01, 15/02 and 15/03 are billed at 800.000đ each.
            var sessions = Enumerable.Range(0, 12)
                .Select(week => TeachingSession.Create(center.Id, math.Id, teacher.Id, room.Id, Local(1, 15).AddDays(7 * week),
                    Local(1, 15).AddDays(7 * week).AddMinutes(90), past)).ToArray();
            await using (var db = new AcademicsDbContext(options))
            {
                db.Subjects.Add(subject);
                db.Teachers.Add(teacher);
                db.Classes.Add(math);
                db.Students.AddRange(an, stranger);
                db.Sessions.AddRange(sessions);
                db.Enrollments.Add(Enrollment.Create(center.Id, math.Id, an.Id, past));
                await db.SaveChangesAsync();
            }
            await using (var finance = new FinanceDbContext(financeOptions))
            {
                finance.ClassFees.Add(ClassFee.Create(center.Id, math.Id, 800_000, new DateOnly(2026, 1, 1), owner.Id, past));
                await finance.SaveChangesAsync();
            }

            PaymentIntentWorkflow Intents(FinanceDbContext db) =>
                new(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), clock);
            TuitionWorkflow Tuition(FinanceDbContext db) =>
                new(new TuitionStore(db, clock, services), new TuitionAcademicsReader(db), new NoNames(), clock);

            // ---- no bank account yet, then the settings are saved ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(TuitionErrors.BankNotConfigured,
                    (await Intents(db).CreateForCenterAsync(center.Id, an.Id, null, owner.Id, default)).Error);
                var saved = await Tuition(db).UpdateSettingsAsync(center.Id, "970436", "0123456789", "Trung Tam QR", 7, true, 0,
                    owner.Id, default);
                Assert.True(saved.IsSuccess);
            }

            // ---- every open charge by default; asking again shows the same code ----
            PaymentIntentDto all;
            await using (var db = new FinanceDbContext(financeOptions))
            {
                all = (await Intents(db).CreateForCenterAsync(center.Id, an.Id, null, owner.Id, default)).Value;
                Assert.Equal(2_400_000, all.Amount);
                Assert.Equal(3, all.ChargeIds.Count);
                Assert.Equal($"{all.Code} HOC PHI", all.TransferContent);
                Assert.Contains("54072400000", all.QrPayload, StringComparison.Ordinal);
                Assert.Contains(all.TransferContent, all.QrPayload, StringComparison.Ordinal);
                Assert.Equal("TRUNG TAM QR", all.Bank.AccountName);
                Assert.Equal(clock.UtcNow.AddDays(7), all.ExpiresAtUtc);
                var again = (await Intents(db).CreateForCenterAsync(center.Id, an.Id, all.ChargeIds, owner.Id, default)).Value;
                Assert.Equal(all.Code, again.Code);
            }

            // ---- the center cannot be asked for charges that are not this student's open ones ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.Equal(TuitionErrors.InvalidCharges, (await Intents(db).CreateForCenterAsync(center.Id, an.Id,
                    [Guid.NewGuid()], owner.Id, default)).Error);
                Assert.Equal(TuitionErrors.NotFound, (await Intents(db).CreateForCenterAsync(center.Id, Guid.NewGuid(), null,
                    owner.Id, default)).Error);
                Assert.Equal(TuitionErrors.NothingToPay, (await Intents(db).CreateForCenterAsync(center.Id, stranger.Id, null,
                    owner.Id, default)).Error);
            }

            // ---- two requests at once: the center guard leaves one code ----
            string firstOfTwo;
            await using (var left = new FinanceDbContext(financeOptions))
            await using (var right = new FinanceDbContext(financeOptions))
            {
                var one = all.ChargeIds.Take(1).ToArray();
                var both = await Task.WhenAll(
                    Intents(left).CreateForCenterAsync(center.Id, an.Id, one, owner.Id, default),
                    Intents(right).CreateForCenterAsync(center.Id, an.Id, one, owner.Id, default));
                Assert.Equal(both[0].Value.Code, both[1].Value.Code);
                Assert.Equal(800_000, both[0].Value.Amount);
                firstOfTwo = both[0].Value.Code;
            }

            // ---- money arrives: the amount drops, a new code replaces the old one, and the old row stays ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                Assert.True((await Tuition(db).RecordPaymentAsync(center.Id, an.Id, 500_000, "Cash", null, null, null, null, null,
                    owner.Id, default)).IsSuccess);
                var smaller = (await Intents(db).CreateForCenterAsync(center.Id, an.Id, null, owner.Id, default)).Value;
                Assert.Equal(1_900_000, smaller.Amount);
                Assert.NotEqual(all.Code, smaller.Code);
                var rows = await db.PaymentIntents.AsNoTracking().Where(x => x.CenterId == center.Id).ToListAsync();
                Assert.Equal(PaymentIntentStatus.Cancelled, rows.Single(x => x.Code == all.Code).Status);
                Assert.Equal(PaymentIntentStatus.Cancelled, rows.Single(x => x.Code == firstOfTwo).Status);
                Assert.Equal(PaymentIntentStatus.Pending, rows.Single(x => x.Code == smaller.Code).Status);
            }

            // ---- an expired code is not shown again ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var before = (await Intents(db).CreateForCenterAsync(center.Id, an.Id, null, owner.Id, default)).Value;
                clock.Now = clock.Now.AddDays(PaymentIntent.LifetimeDays);
                var after = (await Intents(db).CreateForCenterAsync(center.Id, an.Id, null, owner.Id, default)).Value;
                Assert.NotEqual(before.Code, after.Code);
            }

            // ---- a student reaches only their own profile ----
            await using (var db = new FinanceDbContext(financeOptions))
            {
                var mine = (await Intents(db).CreateForStudentAsync(anUser.Id, an.Id, null, default)).Value;
                Assert.Equal(an.Id, mine.StudentId);
                Assert.Equal(TuitionErrors.NotFound, (await Intents(db).CreateForStudentAsync(strangerUser.Id, an.Id, null, default)).Error);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM finance.payment_intents WHERE center_id = @c;
                DELETE FROM finance.receipt_allocations WHERE receipt_id IN (SELECT id FROM finance.receipts WHERE center_id = @c);
                DELETE FROM finance.receipts WHERE center_id = @c;
                DELETE FROM finance.receipt_counters WHERE center_id = @c;
                DELETE FROM finance.tuition_charges WHERE center_id = @c;
                DELETE FROM finance.class_billing_anchors WHERE center_id = @c;
                DELETE FROM finance.class_fees WHERE center_id = @c;
                DELETE FROM finance.finance_settings WHERE center_id = @c;
                DELETE FROM academics.enrollments WHERE center_id = @c;
                DELETE FROM academics.teaching_sessions WHERE center_id = @c;
                DELETE FROM academics.classes WHERE center_id = @c;
                DELETE FROM academics.students WHERE center_id = @c;
                DELETE FROM academics.teachers WHERE center_id = @c;
                DELETE FROM academics.subjects WHERE center_id = @c;
                DELETE FROM organization.rooms WHERE center_id = @c;
                DELETE FROM organization.center_status_history WHERE center_id = @c;
                DELETE FROM organization.centers WHERE id = @c;
                DELETE FROM iam.users WHERE id IN (@owner, @an, @stranger);
                """, connection);
            cleanup.Parameters.AddWithValue("c", center.Id);
            cleanup.Parameters.AddWithValue("owner", owner.Id);
            cleanup.Parameters.AddWithValue("an", anUser.Id);
            cleanup.Parameters.AddWithValue("stranger", strangerUser.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class NoNames : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
