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
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 3b against real PostgreSQL: Auto centers get receipts, exactly once, and every transfer is accounted for.</summary>
[Collection(TuitionDatabase.Name)]
public sealed class RealPostgresAutoBookingTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static DateTimeOffset Local(int month, int day, int hour = 18) =>
        new DateTimeOffset(2026, month, day, hour, 0, 0, Vietnam.GetUtcOffset(new DateTime(2026, month, day))).ToUniversalTime();

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class NoNames : IUserDirectory
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid> userIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    [LocalPostgresFact]
    public async Task Auto_books_each_transfer_once_spreads_it_over_the_qr_charges_and_accounts_for_every_dong()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var clock = new Clock(Local(3, 20, 9));
        var past = Local(1, 1, 8);
        var owner = Guid.NewGuid();
        var marker = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var center = Center.Create("Auto LLC", $"Auto {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"auto-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner, past);
        await using var data = new PaymentTestData(connectionString);
        data.Center(center.Id);
        var room = Room.Create(center.Id, "P1", "Phòng 1", null, owner, past);
        var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, owner, past);
        var teacher = Teacher.Create(center.Id, "GV01", "Cô Lan", past);
        var math = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 20, owner, past);
        var an = Student.Create(center.Id, $"HS{marker}", "Nguyễn An", past);
        var binh = Student.Create(center.Id, $"HB{marker}", "Lê Bình", past);
        var sessions = Enumerable.Range(0, 12).Select(week => TeachingSession.Create(center.Id, math.Id, teacher.Id, room.Id,
            Local(1, 15).AddDays(7 * week), Local(1, 15).AddDays(7 * week).AddMinutes(90), past)).ToArray();
        await using (var organization = new OrganizationDbContext(
                         new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options))
        {
            organization.Centers.Add(center);
            organization.Rooms.Add(room);
            await organization.SaveChangesAsync();
        }
        await using (var academics = new AcademicsDbContext(
                         new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options))
        {
            academics.Subjects.Add(subject);
            academics.Teachers.Add(teacher);
            academics.Classes.Add(math);
            academics.Students.AddRange(an, binh);
            academics.Sessions.AddRange(sessions);
            academics.Enrollments.Add(Enrollment.Create(center.Id, math.Id, an.Id, past));
            await academics.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        TuitionStore Store(FinanceDbContext db) => new(db, clock, services);
        TuitionWorkflow Tuition(FinanceDbContext db) => new(Store(db), new TuitionAcademicsReader(db), new NoNames(), clock);
        PaymentBooker Booker(FinanceDbContext db) => new(new PaymentBookingStore(db), new TuitionAcademicsReader(db), clock, Store(db));
        async Task<int> Auto()
        {
            await using var db = new FinanceDbContext(options);
            return await Booker(db).RunAutoOnceAsync(500, default);
        }

        // ---- the center: 800.000đ a month from 15/01, three cycles billed on 20/03; a QR for all of them ----
        var settings = FinanceSettings.Default(center.Id);
        settings.Update("970436", account, "TRUNG TAM AUTO", 7, true, owner, past);
        PaymentProviderConfig config;
        await using (var db = new FinanceDbContext(options))
        {
            db.ClassFees.Add(ClassFee.Create(center.Id, math.Id, 800_000, new DateOnly(2026, 1, 1), owner, past));
            db.FinanceSettings.Add(settings);
            config = PaymentProviderConfig.Create(center.Id, PaymentProvider.Sepay, "970436", account,
                PaymentIngestionDomainTests.Secrets(protector, center.Id, $"tok-{marker}"), owner, past);
            config.ChangeMode(PaymentProviderMode.Shadow, owner, past);
            db.PaymentProviderConfigs.Add(config);
            await db.SaveChangesAsync();
        }
        PaymentIntentDto qr;
        await using (var db = new FinanceDbContext(options))
            qr = (await new PaymentIntentWorkflow(Store(db), new TuitionAcademicsReader(db), clock)
                .CreateForCenterAsync(center.Id, an.Id, null, owner, default)).Value;
        Assert.Equal(2_400_000, qr.Amount);

        // ---- Auto only follows Shadow; switching records the cutoff ----
        var autoSince = clock.Now;
        await using (var db = new FinanceDbContext(options))
        {
            var row = await db.PaymentProviderConfigs.SingleAsync(x => x.Id == config.Id);
            row.ChangeMode(PaymentProviderMode.Auto, owner, autoSince);
            await db.SaveChangesAsync();
            config = row;
        }

        ReceivedBankTransaction In(string content, long amount, DateTimeOffset at, string? to = null) =>
            new($"{marker}-{Guid.NewGuid():N}", "Vietcombank", to ?? account, null, null, TransferDirection.In, amount, content,
                $"SB{Guid.NewGuid():N}"[..14], at, "{}");
        var before = BankTransaction.Receive(config, In($"{qr.Code} HOC PHI", 800_000, autoSince.AddMinutes(-5)), FetchedBy.Periodic, autoSince);
        var byQr = BankTransaction.Receive(config, In($"{qr.Code} HOC PHI", 1_000_000, autoSince.AddMinutes(1)), FetchedBy.Doorbell, autoSince);
        var byCode = BankTransaction.Receive(config, In($"{binh.Code} HOC PHI", 300_000, autoSince.AddMinutes(2)), FetchedBy.Doorbell, autoSince);
        var elsewhere = BankTransaction.Receive(config, In($"{qr.Code}", 50_000, autoSince.AddMinutes(3), "777000777"), FetchedBy.Doorbell, autoSince);
        var unknown = BankTransaction.Receive(config, In("chuyen tien hoc", 200_000, autoSince.AddMinutes(4)), FetchedBy.Doorbell, autoSince);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.AddRange(before, byQr, byCode, elsewhere, unknown);
            await db.SaveChangesAsync();
        }

        // ---- one pass: each transfer gets exactly one outcome ----
        clock.Now = autoSince.AddMinutes(10);
        Assert.True(await Auto() >= 5);
        async Task<Dictionary<Guid, BankTransaction>> Rows()
        {
            await using var db = new FinanceDbContext(options);
            return await db.BankTransactions.AsNoTracking().Where(x => x.CenterId == center.Id).ToDictionaryAsync(x => x.Id);
        }
        async Task<List<Receipt>> Receipts()
        {
            await using var db = new FinanceDbContext(options);
            return await db.Receipts.AsNoTracking().Include(x => x.Allocations).Where(x => x.CenterId == center.Id).ToListAsync();
        }
        var rows = await Rows();
        Assert.Equal((BankTransactionStatus.Ignored, IgnoreReason.BeforeAuto), (rows[before.Id].Status, rows[before.Id].IgnoreReason));
        Assert.Equal((BankTransactionStatus.Booked, an.Id, (Guid?)qr.Id), (rows[byQr.Id].Status, rows[byQr.Id].StudentId, rows[byQr.Id].IntentId));
        Assert.Equal((BankTransactionStatus.Booked, binh.Id), (rows[byCode.Id].Status, rows[byCode.Id].StudentId));
        Assert.Equal((BankTransactionStatus.Ignored, IgnoreReason.OtherAccount), (rows[elsewhere.Id].Status, rows[elsewhere.Id].IgnoreReason));
        Assert.Equal((BankTransactionStatus.NeedsReview, ReviewReason.NoCode), (rows[unknown.Id].Status, rows[unknown.Id].ReviewReason));
        Assert.All(rows.Values, x => Assert.Null(x.ProcessingId));

        var receipts = await Receipts();
        Assert.Equal(2, receipts.Count);
        var qrReceipt = receipts.Single(x => x.BankTransactionId == byQr.Id);
        Assert.Equal((ReceiptSource.Auto, PaymentMethod.BankTransfer, SystemActors.PaymentFeed, 1_000_000L),
            (qrReceipt.Source, qrReceipt.Method, qrReceipt.CreatedByUserId, qrReceipt.Amount));
        Assert.Equal(1_000_000, qrReceipt.Allocated); // 800.000 for the oldest QR charge, 200.000 for the next
        Assert.All(qrReceipt.Allocations, x => Assert.Contains(x.ChargeId, qr.ChargeIds));
        var codeReceipt = receipts.Single(x => x.BankTransactionId == byCode.Id);
        Assert.Equal((binh.Id, 0L, 300_000L), (codeReceipt.StudentId, codeReceipt.Allocated, codeReceipt.Unallocated));
        await using (var db = new FinanceDbContext(options))
            Assert.Equal(PaymentIntentStatus.Paid, (await db.PaymentIntents.AsNoTracking().SingleAsync(x => x.Id == qr.Id)).Status);

        // ---- the receipt list shows the feed as its author ----
        await using (var db = new FinanceDbContext(options))
        {
            var account2 = (await Tuition(db).StudentAccountAsync(center.Id, an.Id, default)).Value;
            var shown = Assert.Single(account2.Receipts);
            Assert.Equal(("Auto", "Tự động (SePay)"), (shown.Source, shown.CreatedByName));
            Assert.Equal(1_400_000, account2.Owed);
        }

        // ---- again, and two Workers at once on a new transfer: still one receipt each ----
        await Auto();
        Assert.DoesNotContain((await Rows()).Values, x => x.Status == BankTransactionStatus.Received);
        Assert.Equal(2, (await Receipts()).Count);
        var late = BankTransaction.Receive(config, In($"{qr.Code} HOC PHI", 600_000, clock.Now), FetchedBy.Doorbell, clock.Now);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.Add(late);
            await db.SaveChangesAsync();
        }
        await Task.WhenAll(Auto(), Auto());
        receipts = await Receipts();
        Assert.Single(receipts, x => x.BankTransactionId == late.Id);
        Assert.Equal(3, receipts.Count);

        // ---- the database refuses a second Auto receipt for a transfer, whatever the code does ----
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var duplicate = new NpgsqlCommand("""
                INSERT INTO finance.receipts (id, center_id, number, student_id, method, amount, received_on, created_by_user_id,
                    created_at_utc, version, bank_transaction_id, source)
                VALUES (@id, @center, 9999, @student, 'BankTransfer', 1, '2026-03-20', @actor, now(), 0, @transfer, 'Auto')
                """, connection);
            duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
            duplicate.Parameters.AddWithValue("center", center.Id);
            duplicate.Parameters.AddWithValue("student", an.Id);
            duplicate.Parameters.AddWithValue("actor", SystemActors.PaymentFeed);
            duplicate.Parameters.AddWithValue("transfer", byQr.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        }

        // ---- with Auto on, a hand-written bank transfer is refused; cash is still fine ----
        await using (var db = new FinanceDbContext(options))
        {
            Assert.Equal(TuitionErrors.BankTransferIsAutomatic,
                (await Tuition(db).RecordPaymentAsync(center.Id, an.Id, 100_000, "BankTransfer", null, null, null, null, null, owner, default)).Error);
            Assert.True((await Tuition(db).RecordPaymentAsync(center.Id, an.Id, 100_000, "Cash", null, null, null, null, null, owner, default)).IsSuccess);
        }

        // ---- voiding the Auto receipt sends the transfer back to the queue; a replay cannot book it again ----
        await using (var db = new FinanceDbContext(options))
            Assert.True((await Tuition(db).VoidReceiptAsync(center.Id, qrReceipt.Id, "Ghi nhầm", owner, default)).IsSuccess);
        rows = await Rows();
        Assert.Equal((BankTransactionStatus.NeedsReview, ReviewReason.VoidedReceipt, (Guid?)null),
            (rows[byQr.Id].Status, rows[byQr.Id].ReviewReason, rows[byQr.Id].StudentId));
        await using (var db = new FinanceDbContext(options))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE finance.bank_transactions SET status = 'Received', review_reason = NULL WHERE id = {byQr.Id}");
        await Auto();
        Assert.Single(await Receipts(), x => x.Source == ReceiptSource.Auto && x.BankTransactionId == byQr.Id);
        rows = await Rows();
        Assert.Equal(BankTransactionStatus.Received, rows[byQr.Id].Status);
        Assert.NotNull(rows[byQr.Id].LastError);
        Assert.Equal(1, rows[byQr.Id].AttemptCount);

        // ---- K17: every dong that came in is a live Auto receipt, waiting, or ignored with a reason ----
        receipts = await Receipts();
        var incoming = rows.Values.Sum(x => x.Amount);
        var booked = receipts.Where(x => x.Source == ReceiptSource.Auto && !x.IsVoided).Sum(x => x.Amount);
        var waiting = rows.Values.Where(x => x.Status is BankTransactionStatus.Received or BankTransactionStatus.NeedsReview
            or BankTransactionStatus.Failed or BankTransactionStatus.Held).Sum(x => x.Amount);
        var ignored = rows.Values.Where(x => x.Status == BankTransactionStatus.Ignored).Sum(x => x.Amount);
        Assert.Equal(incoming, booked + waiting + ignored);
    }
}
