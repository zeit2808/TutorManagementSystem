using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Domain.Rooms;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Đợt 4 against real PostgreSQL: the "whose money is it" queue, its suggestions and the confirmation email.</summary>
[Collection(TuitionDatabase.Name)]
public sealed class RealPostgresPaymentQueueTests
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
    public async Task Staff_assign_with_suggestions_owners_decide_the_rest_and_parents_are_told()
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
        var center = Center.Create("Queue LLC", $"Queue {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"queue-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner, past);
        var anUser = User.CreateExternal($"an-{marker.ToLowerInvariant()}@example.invalid", past);
        await using var data = new PaymentTestData(connectionString);
        data.Center(center.Id);
        data.Email(anUser.Email);
        var room = Room.Create(center.Id, "P1", "Phòng 1", null, owner, past);
        var subject = Subject.Create(center.Id, "MATH", $"Toán {marker}", null, owner, past);
        var teacher = Teacher.Create(center.Id, "GV01", "Cô Lan", past);
        var math = TeachingClass.Create(center.Id, subject.Id, "T9A", "Toán 9A", 20, owner, past);
        var an = Student.CreateForUser(center.Id, anUser.Id, $"HS{marker}", "Nguyễn An", past);
        var binh = Student.Create(center.Id, $"HB{marker}", "Lê Bình", past);
        var sessions = Enumerable.Range(0, 12).Select(week => TeachingSession.Create(center.Id, math.Id, teacher.Id, room.Id,
            Local(1, 15).AddDays(7 * week), Local(1, 15).AddDays(7 * week).AddMinutes(90), past)).ToArray();
        await using (var identity = new IdentityDbContext(
                         new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            identity.Users.Add(anUser);
            await identity.SaveChangesAsync();
        }
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
        PaymentQueueWorkflow Queue(FinanceDbContext db) => new(Store(db), new TuitionAcademicsReader(db), clock);
        var settings = FinanceSettings.Default(center.Id);
        settings.Update("970436", account, "TRUNG TAM QUEUE", 7, true, owner, past);
        PaymentProviderConfig config;
        await using (var db = new FinanceDbContext(options))
        {
            db.ClassFees.Add(ClassFee.Create(center.Id, math.Id, 800_000, new DateOnly(2026, 1, 1), owner, past));
            db.FinanceSettings.Add(settings);
            config = PaymentProviderConfig.Create(center.Id, PaymentProvider.Sepay, "970436", account,
                PaymentIngestionDomainTests.Secrets(protector, center.Id, $"tok-{marker}"), owner, past);
            config.ChangeMode(PaymentProviderMode.Shadow, owner, past);
            config.ChangeMode(PaymentProviderMode.Auto, owner, clock.Now.AddHours(-1));
            db.PaymentProviderConfigs.Add(config);
            await db.SaveChangesAsync();
        }
        PaymentIntentDto qr;
        await using (var db = new FinanceDbContext(options))
            qr = (await new PaymentIntentWorkflow(Store(db), new TuitionAcademicsReader(db), clock)
                .CreateForCenterAsync(center.Id, an.Id, null, owner, default)).Value;

        BankTransaction Waiting(string content, long amount, BankTransactionStatus status = BankTransactionStatus.NeedsReview)
        {
            var row = BankTransaction.Receive(config, new ReceivedBankTransaction($"{marker}-{Guid.NewGuid():N}", "Vietcombank",
                account, null, null, TransferDirection.In, amount, content, $"SB{Guid.NewGuid():N}"[..14], clock.Now.AddMinutes(-30), "{}"),
                FetchedBy.Doorbell, clock.Now);
            if (status == BankTransactionStatus.NeedsReview) row.SendToReview(ReviewReason.NoCode);
            return row;
        }
        var near = qr.Code[..5] + (qr.Code[5] == 'A' ? 'B' : 'A') + qr.Code[6..];
        var sameAmount = Waiting("chuyen tien hoc", 2_400_000);
        var nearCode = Waiting($"{near} HOC PHI", 500_000);
        var splitCode = Waiting($"HB {marker} hoc phi", 300_000);
        var refund = Waiting("hoan tien", 100_000);
        var failing = Waiting("loi", 50_000, BankTransactionStatus.Received);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.AddRange(sameAmount, nearCode, splitCode, refund, failing);
            await db.SaveChangesAsync();
        }
        var held = BankTransaction.HoldAsDuplicate(config, new ReceivedBankTransaction($"{marker}-held", "Vietcombank", account,
            null, null, TransferDirection.In, 300_000, "x", splitCode.ReferenceCode, clock.Now, "{}"), splitCode.Id,
            FetchedBy.Nightly, clock.Now);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.Add(held);
            await db.SaveChangesAsync();
        }

        // ---- the queue with suggestions; nothing is assigned by itself ----
        TuitionPage<BankTransferDto> page;
        await using (var db = new FinanceDbContext(options))
            page = (await Queue(db).ListAsync(center.Id, "Queue", 1, 25, default)).Value;
        Assert.Equal(5, page.TotalCount);
        var byId = page.Items.ToDictionary(x => x.Id);
        Assert.Contains(byId[sameAmount.Id].Suggestions, x => x.StudentId == an.Id && x.Reason.Contains("đúng số tiền", StringComparison.Ordinal));
        Assert.Contains(byId[nearCode.Id].Suggestions, x => x.StudentId == an.Id && x.Reason.Contains("gần giống", StringComparison.Ordinal));
        Assert.Contains(byId[splitCode.Id].Suggestions, x => x.StudentId == binh.Id);
        Assert.Empty(byId[refund.Id].Suggestions);
        Assert.Equal("Held", byId[held.Id].Status);
        Assert.Empty(byId[held.Id].Suggestions);
        await using (var db = new FinanceDbContext(options))
            Assert.Equal(QueueErrors.InvalidStatus, (await Queue(db).ListAsync(center.Id, "Weird", 1, 25, default)).Error);

        // ---- staff assign: a receipt, the QR charges paid, the parent told ----
        BankTransferDto assigned;
        await using (var db = new FinanceDbContext(options))
            assigned = (await Queue(db).AssignAsync(center.Id, sameAmount.Id, an.Id, qr.ChargeIds, byId[sameAmount.Id].Version,
                owner, default)).Value;
        Assert.Equal(("Booked", an.Id), (assigned.Status, assigned.StudentId));
        Assert.NotNull(assigned.ReceiptNumber);
        Receipt receipt;
        await using (var db = new FinanceDbContext(options))
        {
            receipt = await db.Receipts.AsNoTracking().Include(x => x.Allocations).SingleAsync(x => x.BankTransactionId == sameAmount.Id);
            Assert.Equal((ReceiptSource.Manual, owner, 2_400_000L, 2_400_000L), (receipt.Source, receipt.CreatedByUserId, receipt.Amount, receipt.Allocated));
            Assert.Equal("transfer_assigned", (await db.PaymentAuditEvents.AsNoTracking().SingleAsync(x => x.TargetId == sameAmount.Id)).Action);
        }
        await using (var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            var mail = await identity.EmailOutboxMessages.AsNoTracking().SingleAsync(x => x.Recipient == anUser.Email);
            Assert.StartsWith("Đã nhận học phí", mail.Subject, StringComparison.Ordinal);
            Assert.Contains("2.400.000đ", mail.Body, StringComparison.Ordinal);
            Assert.Contains("đã đóng đủ", mail.Body, StringComparison.Ordinal);
        }

        // ---- the same transfer twice, a stale page, two staff at once ----
        await using (var db = new FinanceDbContext(options))
        {
            Assert.Equal(QueueErrors.Version, (await Queue(db).AssignAsync(center.Id, sameAmount.Id, an.Id, null,
                byId[sameAmount.Id].Version, owner, default)).Error);
            Assert.Equal(QueueErrors.NotInQueue, (await Queue(db).AssignAsync(center.Id, sameAmount.Id, an.Id, null,
                assigned.Version, owner, default)).Error);
        }
        async Task<TutorManagement.SharedKernel.Primitives.Result<BankTransferDto>> AssignNear(Guid student)
        {
            await using var db = new FinanceDbContext(options);
            return await Queue(db).AssignAsync(center.Id, nearCode.Id, student, null, byId[nearCode.Id].Version, Guid.NewGuid(), default);
        }
        var both = await Task.WhenAll(AssignNear(an.Id), AssignNear(binh.Id));
        Assert.Equal(1, both.Count(x => x.IsSuccess));
        await using (var db = new FinanceDbContext(options))
            Assert.Single(await db.Receipts.AsNoTracking().Where(x => x.BankTransactionId == nearCode.Id).ToListAsync());

        // ---- not tuition: owner or manager, with a reason ----
        await using (var db = new FinanceDbContext(options))
        {
            Assert.Equal(QueueErrors.InvalidReason, (await Queue(db).IgnoreAsync(center.Id, refund.Id, "x", 0, owner, default)).Error);
            var ignored = (await Queue(db).IgnoreAsync(center.Id, refund.Id, "Hoàn tiền cho phụ huynh", byId[refund.Id].Version, owner, default)).Value;
            Assert.Equal(("Ignored", "NotTuition"), (ignored.Status, ignored.IgnoreReason));
        }

        // ---- a voided receipt sends the money back; it can then go to the right student ----
        await using (var db = new FinanceDbContext(options))
            Assert.True((await new TuitionWorkflow(Store(db), new TuitionAcademicsReader(db), new NoNames(), clock)
                .VoidReceiptAsync(center.Id, receipt.Id, "Gán nhầm học sinh", owner, default)).IsSuccess);
        await using (var db = new FinanceDbContext(options))
        {
            var back = (await Queue(db).ListAsync(center.Id, "NeedsReview", 1, 25, default)).Value.Items.Single(x => x.Id == sameAmount.Id);
            Assert.Equal("VoidedReceipt", back.ReviewReason);
            Assert.True((await Queue(db).AssignAsync(center.Id, sameAmount.Id, binh.Id, null, back.Version, owner, default)).IsSuccess);
        }
        await using (var db = new FinanceDbContext(options))
        {
            var receipts = await db.Receipts.AsNoTracking().Where(x => x.BankTransactionId == sameAmount.Id).ToListAsync();
            Assert.Equal(2, receipts.Count);
            Assert.Single(receipts, x => x.VoidedAtUtc == null && x.StudentId == binh.Id);
        }

        // ---- the owner decides a held copy ----
        await using (var db = new FinanceDbContext(options))
        {
            var decided = (await Queue(db).ResolveHeldAsync(center.Id, held.Id, true, "Sao kê chỉ có một khoản", byId[held.Id].Version,
                owner, default)).Value;
            Assert.Equal(("Ignored", "Duplicate"), (decided.Status, decided.IgnoreReason));
        }

        // ---- five failures make it Failed; the owner retries it ----
        await using (var db = new FinanceDbContext(options))
        {
            var lease = Guid.NewGuid();
            for (var attempt = 0; attempt < PaymentBookingStore.MaxAttempts; attempt++)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE finance.bank_transactions SET processing_id = {lease} WHERE id = {failing.Id}");
                await new PaymentBookingStore(db).FailAsync(failing.Id, lease, "boom", clock.Now, default);
            }
        }
        await using (var db = new FinanceDbContext(options))
        {
            var failed = (await Queue(db).ListAsync(center.Id, "Failed", 1, 25, default)).Value.Items.Single();
            Assert.Equal(("Failed", "boom"), (failed.Status, failed.LastError));
            var retried = (await Queue(db).RetryAsync(center.Id, failing.Id, failed.Version, owner, default)).Value;
            Assert.Equal("Received", retried.Status);
        }

        // ---- the bank feed's own bookings tell the parent too ----
        var viaQr = BankTransaction.Receive(config, new ReceivedBankTransaction($"{marker}-auto", "Vietcombank", account, null, null,
            TransferDirection.In, 100_000, $"{qr.Code} HOC PHI", null, clock.Now, "{}"), FetchedBy.Doorbell, clock.Now);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.Add(viaQr);
            await db.SaveChangesAsync();
        }
        await using (var db = new FinanceDbContext(options))
            await new PaymentBooker(new PaymentBookingStore(db), new TuitionAcademicsReader(db), clock, Store(db)).RunAutoOnceAsync(500, default);
        await using (var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            var mails = await identity.EmailOutboxMessages.AsNoTracking().Where(x => x.Recipient == anUser.Email).ToListAsync();
            // One for the assigned 2.400.000đ, one for the feed's 100.000đ, and one more if An won the race above.
            Assert.Equal(both.Single(x => x.IsSuccess).Value.StudentId == an.Id ? 3 : 2, mails.Count);
            Assert.Contains(mails, x => x.Body.Contains("2.400.000đ", StringComparison.Ordinal));
            Assert.Contains(mails, x => x.Body.Contains("100.000đ", StringComparison.Ordinal));
        }
    }
}
