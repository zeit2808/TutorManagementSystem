using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Academics.Domain.Teaching;
using TutorManagement.Modules.Academics.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Contracts.Members;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Đợt 5 rules that need no database: the nightly slot and the monitor's alert rules.</summary>
public sealed class PaymentOperationsRuleTests
{
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(7)).ToUniversalTime();

    [Fact]
    public void Nightly_slot_is_the_next_2am_in_Vietnam()
    {
        Assert.Equal(Local(11, 2), PaymentReconciler.NextNightly(Local(10, 23, 59)));
        Assert.Equal(Local(11, 2), PaymentReconciler.NextNightly(Local(11, 1, 59)));
        Assert.Equal(Local(12, 2), PaymentReconciler.NextNightly(Local(11, 2)));
        Assert.Equal(Local(12, 2), PaymentReconciler.NextNightly(Local(11, 2, 1)));
        _ = Vietnam;
    }

    private static MonitorConfig Config(Guid center, DateTimeOffset now, long bad = 0, DateTimeOffset? lastBad = null,
        long? baseline = null, bool open = false, string? error = null, DateTimeOffset? pulled = null,
        PaymentProviderMode mode = PaymentProviderMode.Auto) =>
        new(Guid.Parse("00000000-0000-0000-0000-0000000000c1"), center, mode, error, pulled ?? now, now.AddDays(-30), bad, lastBad,
            baseline, open);

    private static MonitorSnapshot Snapshot(params MonitorConfig[] configs) => new(configs, [], [], [], [], []);

    [Fact]
    public void Bad_signatures_alert_on_a_burst_stay_open_while_they_continue_and_close_after_a_quiet_day()
    {
        var center = Guid.NewGuid();
        var now = Local(10, 12);
        IReadOnlyList<AlertDraft> Drafts(MonitorConfig config) => PaymentMonitor.Decide(Snapshot(config), now)[center];

        Assert.Empty(Drafts(Config(center, now, bad: 4, lastBad: now.AddMinutes(-1))));
        var burst = Assert.Single(Drafts(Config(center, now, bad: 5, lastBad: now.AddMinutes(-1))));
        Assert.Equal(AlertKinds.BadSignatures, burst.Kind);
        Assert.Equal(5, burst.Metric);
        // Old bad signatures do not count as a burst.
        Assert.Empty(Drafts(Config(center, now, bad: 50, lastBad: now.AddHours(-2))));
        // While open, one more bad signature keeps it open without moving the baseline.
        var kept = Assert.Single(Drafts(Config(center, now, bad: 6, lastBad: now.AddHours(-3), baseline: 5, open: true)));
        Assert.Null(kept.Metric);
        Assert.Empty(Drafts(Config(center, now, bad: 6, lastBad: now.AddHours(-25), baseline: 5, open: true)));
        // After it closed, only a new burst since the last alert opens it again.
        Assert.Empty(Drafts(Config(center, now, bad: 8, lastBad: now.AddMinutes(-1), baseline: 5)));
        Assert.Equal(10, Assert.Single(Drafts(Config(center, now, bad: 10, lastBad: now.AddMinutes(-1), baseline: 5))).Metric);
    }

    private sealed class Answer(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task A_SePay_field_we_have_not_seen_is_reported_and_the_known_ones_are_not()
    {
        const string body = """
            {"status":200,"data":[{"id":"1","bank_brand_name":"Vietcombank","account_number":"0123","transaction_date":"2026-11-10 09:00:00",
              "amount_out":"0.00","amount_in":"100000.00","accumulated":"0.00","transaction_content":"HOC PHI","reference_number":"FT1",
              "code":null,"bank_account_id":"9","va":null,"va_id":null,"transfer_type":"in","webhook_success":1,"new_flag":true}],
             "meta":{"pagination":{"has_more":false}}}
            """;
        using var http = new HttpClient(new Answer(body));
        var page = await new TutorManagement.Modules.Finance.Infrastructure.Payments.SepayBankFeed(http,
            new TutorManagement.Modules.Finance.Infrastructure.Payments.SepayOptions()).ListAsync("token", new BankFeedQuery(null, null, 1, 100), default);
        Assert.Single(page.Transactions);
        Assert.Equal(["new_flag"], page.UnknownFields);
    }

    [Fact]
    public void SePay_down_alerts_after_an_hour_and_an_Off_connection_alerts_nothing()
    {
        var center = Guid.NewGuid();
        var now = Local(10, 12);
        Assert.Empty(PaymentMonitor.Decide(Snapshot(Config(center, now, error: "SePay answered 503.", pulled: now.AddMinutes(-59))), now)[center]);
        var down = Assert.Single(PaymentMonitor.Decide(
            Snapshot(Config(center, now, error: "SePay answered 503.", pulled: now.AddMinutes(-61))), now)[center]);
        Assert.Equal(AlertKinds.ApiDown, down.Kind);
        Assert.Contains("503", down.Detail, StringComparison.Ordinal);
        // An Off connection still gets an entry, so its old alerts close.
        Assert.Empty(PaymentMonitor.Decide(Snapshot(Config(center, now, bad: 9, lastBad: now, error: "x", pulled: now.AddDays(-1),
            mode: PaymentProviderMode.Off)), now)[center]);
    }
}

/// <summary>Đợt 5 against real PostgreSQL: the nightly reconciliation, owner alerts without repeats, and the digest.</summary>
[Collection(TuitionDatabase.Name)]
public sealed class RealPostgresReconciliationTests
{
    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Owners(Guid owner) : ICenterMemberDirectory
    {
        public Task<CenterMemberSummary?> FindActiveAsync(Guid centerId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<CenterMemberSummary?>(null);

        public Task<IReadOnlyList<Guid>> ListActiveUserIdsAsync(Guid centerId, IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([owner]);
    }

    /// <summary>The provider's answer for every page: always the same list, newest first.</summary>
    private sealed class Feed : IBankFeedProvider
    {
        public List<ReceivedBankTransaction> Transactions { get; } = [];
        public List<BankFeedQuery> Queries { get; } = [];
        public bool Down { get; set; }
        public PaymentProvider Provider => PaymentProvider.Sepay;

        public Task<BankFeedPage> ListAsync(string apiToken, BankFeedQuery query, CancellationToken token)
        {
            Queries.Add(query);
            if (Down) throw new BankFeedException("SePay answered 503.", true);
            return Task.FromResult(new BankFeedPage([.. Transactions.OrderByDescending(x => x.TransactedAtUtc)], false));
        }
    }

    private static DateTimeOffset Local(int day, int hour, int minute = 0) =>
        new DateTimeOffset(2026, 3, day, hour, minute, 0, TimeSpan.FromHours(7)).ToUniversalTime();

    [LocalPostgresFact]
    public async Task Reconciliation_finds_every_gap_alerts_once_closes_what_was_fixed_and_the_digest_repeats_what_is_open()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var clock = new Clock(Local(20, 2, 5));
        var past = Local(1, 8);
        var marker = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var ownerUser = User.CreateExternal($"recon-owner-{marker.ToLowerInvariant()}@example.invalid", past);
        var center = Center.Create("Recon LLC", $"Recon {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"recon-{marker}@example.invalid", "0900000000", past);
        center.Approve(ownerUser.Id, past);
        await using var data = new PaymentTestData(connectionString);
        data.Center(center.Id);
        data.Email(ownerUser.Email);
        var student = Student.Create(center.Id, $"HR{marker}", "Trần Đối Soát", past);
        await using (var identity = new IdentityDbContext(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options))
        {
            identity.Users.Add(ownerUser);
            await identity.SaveChangesAsync();
        }
        await using (var organization = new OrganizationDbContext(
                         new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options))
        {
            organization.Centers.Add(center);
            await organization.SaveChangesAsync();
        }
        await using (var academics = new AcademicsDbContext(new DbContextOptionsBuilder<AcademicsDbContext>().UseNpgsql(connectionString).Options))
        {
            academics.Students.Add(student);
            await academics.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var feed = new Feed();
        PaymentProviderConfig config;
        await using (var db = new FinanceDbContext(options))
        {
            config = PaymentProviderConfig.Create(center.Id, PaymentProvider.Sepay, "970436", account,
                PaymentIngestionDomainTests.Secrets(protector, center.Id, $"tok-{marker}"), ownerUser.Id, past);
            config.ChangeMode(PaymentProviderMode.Shadow, ownerUser.Id, past);
            config.ChangeMode(PaymentProviderMode.Auto, ownerUser.Id, past.AddDays(1));
            db.PaymentProviderConfigs.Add(config);
            await db.SaveChangesAsync();
        }

        ReceivedBankTransaction Api(string id, long amount, string content, string reference, DateTimeOffset at) =>
            new($"{marker}-{id}", "Vietcombank", account, null, null, TransferDirection.In, amount, content, reference, at, "{}");
        var number = 900;
        Receipt ReceiptFor(BankTransaction row, long amount, ReceiptSource source = ReceiptSource.Auto) =>
            Receipt.CreateFromBank(center.Id, ++number, student.Id, amount, DateOnly.FromDateTime(row.TransactedAtUtc.UtcDateTime), null,
                row.Id, source, SystemActors.PaymentFeed, clock.Now);

        // The ledger as the Worker left it, with four kinds of damage nobody should ever see.
        var good = Api("good", 500_000, $"HR{marker} HOC PHI", $"RG{marker}", Local(19, 9));
        var noReceipt = Api("noreceipt", 300_000, "HOC PHI THANG 3", $"RN{marker}", Local(19, 10));
        var review = Api("review", 200_000, "CHUYEN TIEN", $"RV{marker}", Local(19, 11));
        var wrongAmount = Api("wrong", 400_000, $"HR{marker}", $"RW{marker}", Local(19, 12));
        BankTransaction goodRow, noReceiptRow, reviewRow, wrongRow;
        await using (var db = new FinanceDbContext(options))
        {
            goodRow = BankTransaction.Receive(config, good, FetchedBy.Doorbell, Local(19, 9, 1));
            noReceiptRow = BankTransaction.Receive(config, noReceipt, FetchedBy.Doorbell, Local(19, 10, 1));
            reviewRow = BankTransaction.Receive(config, review, FetchedBy.Doorbell, Local(19, 11, 1));
            wrongRow = BankTransaction.Receive(config, wrongAmount, FetchedBy.Doorbell, Local(19, 12, 1));
            foreach (var row in new[] { goodRow, noReceiptRow, wrongRow }) row.MarkBooked(student.Id, null, clock.Now);
            reviewRow.SendToReview(ReviewReason.NoCode);
            db.BankTransactions.AddRange(goodRow, noReceiptRow, reviewRow, wrongRow);
            db.Receipts.AddRange(ReceiptFor(goodRow, 500_000), ReceiptFor(reviewRow, 200_000), ReceiptFor(wrongRow, 450_000));
            // Webhooks: one for a transfer we have, one SePay's API never listed, one for money going out.
            db.PaymentWebhookDeliveries.AddRange(
                PaymentWebhookDelivery.Create(config.Id, 7001, good.ReferenceCode, 500_000, """{"transferType":"in"}""", Local(19, 9)),
                PaymentWebhookDelivery.Create(config.Id, 7002, $"RX{marker}", 250_000, """{"transferType":"in"}""", Local(19, 13)),
                PaymentWebhookDelivery.Create(config.Id, 7003, $"RO{marker}", 90_000, """{"transferType":"out"}""", Local(19, 14)));
            await db.SaveChangesAsync();
        }
        // SePay: the four we stored (one now reported differently), plus one the 5-minute pull never saw.
        var missed = Api("missed", 150_000, "HOC PHI EM AN", $"RM{marker}", Local(19, 15));
        feed.Transactions.AddRange([good with { Content = good.Content + " (sửa)" }, noReceipt, review, wrongAmount, missed,
            Api("old", 99_000, "QUA CU", $"RO2{marker}", Local(1, 9))]);

        PaymentOpsStore Ops(FinanceDbContext db) => new(db, new PaymentPullStore(db));
        PaymentAlertBook Book(FinanceDbContext db) => new(Ops(db), new TuitionStore(db, clock, services), new Owners(ownerUser.Id), clock);
        async Task Reconcile()
        {
            await using var db = new FinanceDbContext(options);
            await new PaymentReconciler(Ops(db), [feed], protector, Book(db), clock).RunOnceAsync(5, default, config.Id);
        }
        async Task Monitor()
        {
            await using var db = new FinanceDbContext(options);
            await new PaymentMonitor(Ops(db), Book(db), clock).RunOnceAsync(default, center.Id);
        }
        async Task<List<PaymentAlert>> Alerts()
        {
            await using var db = new FinanceDbContext(options);
            return await db.PaymentAlerts.AsNoTracking().Where(x => x.CenterId == center.Id).ToListAsync();
        }
        async Task<List<(string Subject, string Body)>> Emails()
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "SELECT subject, body FROM iam.email_outbox_messages WHERE recipient = @to ORDER BY created_at_utc, subject", connection);
            command.Parameters.AddWithValue("to", ownerUser.Email);
            var result = new List<(string, string)>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) result.Add((reader.GetString(0), reader.GetString(1)));
            return result;
        }
        async Task Sql(string sql)
        {
            await using var db = new FinanceDbContext(options);
            await db.Database.ExecuteSqlRawAsync(sql, new NpgsqlParameter("config", config.Id), new NpgsqlParameter("center", center.Id));
        }

        // 1. A new connection gets its first slot at 02:00 tomorrow; nothing runs before that.
        await Reconcile();
        Assert.Empty(feed.Queries);
        await using (var db = new FinanceDbContext(options))
            Assert.Equal(Local(21, 2), (await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == config.Id)).NextReconcileAtUtc);

        // 2. The owner asks for one now.
        await using (var db = new FinanceDbContext(options))
            Assert.True((await new PaymentOpsWorkflow(Ops(db), Ops(db), clock).RequestAsync(center.Id, default)).Value);
        await Reconcile();
        var query = Assert.Single(feed.Queries);
        Assert.Null(query.SinceId);
        Assert.Equal(clock.Now - PaymentReconciler.Window, query.FromUtc);

        ReconciliationRunDto run;
        await using (var db = new FinanceDbContext(options))
        {
            var runs = await new PaymentOpsWorkflow(Ops(db), Ops(db), clock).ListRunsAsync(center.Id, 1, 20, default);
            run = (await new PaymentOpsWorkflow(Ops(db), Ops(db), clock).GetRunAsync(center.Id, Assert.Single(runs.Items).Id, default)).Value;
        }
        Assert.Equal("Manual", run.Trigger);
        Assert.Equal("Completed", run.Status);
        Assert.Equal(5, run.ScannedCount); // the transfer older than 3 days is not part of the window
        Assert.Equal(1, run.MissedCount);
        Assert.Equal(1, run.ChangedCount);
        Assert.Equal(500_000 + 300_000 + 200_000 + 400_000 + 150_000, run.IncomingTotal);
        Assert.Equal(1_200_000, run.BookedTotal);
        Assert.Equal(950_000, run.BookedReceiptTotal);
        Assert.Equal(200_000 + 150_000, run.PendingTotal);
        Assert.False(run.TotalsAgree);
        var kinds = run.Findings.Select(x => x.Kind).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[]
        {
            FindingKinds.BookedWithoutReceipt, FindingKinds.ChangedAfterStore, FindingKinds.DoorbellWithoutTransfer,
            FindingKinds.MissedByPull, FindingKinds.ReceiptAmountMismatch, FindingKinds.ReceiptWithoutBookedTransfer,
            FindingKinds.TotalsMismatch
        }, kinds);
        Assert.Equal(1_200_000 - 950_000, run.Findings.Single(x => x.Kind == FindingKinds.TotalsMismatch).Amount);

        // The missed transfer is now in the inbox like any other, ready for the booker.
        await using (var db = new FinanceDbContext(options))
        {
            var stored = await db.BankTransactions.AsNoTracking().SingleAsync(x => x.ProviderTxnId == missed.ProviderTxnId);
            Assert.Equal(BankTransactionStatus.Received, stored.Status);
            Assert.Equal(FetchedBy.Manual, stored.FetchedBy);
            // K19: the stored copy is kept.
            Assert.Equal(good.Content, (await db.BankTransactions.AsNoTracking().SingleAsync(x => x.Id == goodRow.Id)).Content);
            Assert.Null((await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == config.Id)).ReconcileRequestedAtUtc);
        }

        // One email for all seven, one alert each.
        var alerts = await Alerts();
        Assert.Equal(7, alerts.Count);
        Assert.All(alerts, x => Assert.Equal(PaymentAlertState.Open, x.State));
        var first = Assert.Single(await Emails());
        Assert.Contains("7 vấn đề mới", first.Subject, StringComparison.Ordinal);
        Assert.Contains("Webhook có giao dịch nhưng API SePay không có", first.Body, StringComparison.Ordinal);

        // 3. The monitor sees the same changed transfer: same alert, no new email.
        await Monitor();
        Assert.Equal(7, (await Alerts()).Count);
        Assert.Single(await Emails());

        // 4. Staff fix two problems, the owner looks at the changed transfer, and the night run comes.
        await using (var db = new FinanceDbContext(options))
        {
            db.Receipts.Add(ReceiptFor(noReceiptRow, 300_000, ReceiptSource.Manual));
            await db.SaveChangesAsync();
            var wrong = await db.Receipts.SingleAsync(x => x.BankTransactionId == wrongRow.Id);
            wrong.Void("Ghi nhầm số tiền", ownerUser.Id, clock.Now);
            await db.SaveChangesAsync();
            db.Receipts.Add(ReceiptFor(wrongRow, 400_000, ReceiptSource.Manual)); // one Auto receipt per transfer, ever
            await db.SaveChangesAsync();
        }
        var changedAlert = (await Alerts()).Single(x => x.Fingerprint.StartsWith(FindingKinds.ChangedAfterStore, StringComparison.Ordinal));
        await using (var db = new FinanceDbContext(options))
        {
            var workflow = new PaymentOpsWorkflow(Ops(db), Ops(db), clock);
            Assert.Equal("Acknowledged", (await workflow.AcknowledgeAsync(center.Id, changedAlert.Id, ownerUser.Id, default)).Value.State);
            Assert.Equal(PaymentOpsErrors.AlertNotOpen, (await workflow.AcknowledgeAsync(center.Id, changedAlert.Id, ownerUser.Id, default)).Error);
            Assert.Equal(PaymentOpsErrors.AlertNotFound, (await workflow.AcknowledgeAsync(Guid.NewGuid(), changedAlert.Id, ownerUser.Id, default)).Error);
        }
        clock.Now = Local(21, 2, 1);
        await Reconcile();
        alerts = await Alerts();
        Assert.Equal(PaymentAlertState.Resolved, alerts.Single(x => x.Fingerprint == $"{FindingKinds.BookedWithoutReceipt}:{noReceiptRow.Id}").State);
        Assert.Equal(PaymentAlertState.Resolved, alerts.Single(x => x.Fingerprint == $"{FindingKinds.ReceiptAmountMismatch}:{wrongRow.Id}").State);
        Assert.Equal(PaymentAlertState.Resolved, alerts.Single(x => x.Fingerprint == FindingKinds.TotalsMismatch + ":").State);
        Assert.Equal(PaymentAlertState.Acknowledged, alerts.Single(x => x.Id == changedAlert.Id).State);
        Assert.Equal(PaymentAlertState.Open, alerts.Single(x => x.Fingerprint.StartsWith(FindingKinds.MissedByPull, StringComparison.Ordinal)).State);
        Assert.Single(await Emails());
        await using (var db = new FinanceDbContext(options))
        {
            var nightly = await db.ReconciliationRuns.AsNoTracking().Where(x => x.CenterId == center.Id)
                .OrderByDescending(x => x.StartedAtUtc).FirstAsync();
            Assert.Equal(ReconciliationTrigger.Nightly, nightly.Trigger);
            Assert.Equal(0, nightly.MissedCount); // stored by the first run
            Assert.Equal(nightly.BookedTotal, nightly.BookedReceiptTotal);
            Assert.Equal(Local(22, 2), (await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == config.Id)).NextReconcileAtUtc);
        }

        // 5. SePay down at night: the ledger checks still run and the run says why the scan failed.
        feed.Down = true;
        clock.Now = Local(22, 2, 1);
        await Reconcile();
        await using (var db = new FinanceDbContext(options))
        {
            var failed = await db.ReconciliationRuns.AsNoTracking().Where(x => x.CenterId == center.Id)
                .OrderByDescending(x => x.StartedAtUtc).FirstAsync();
            Assert.Equal(ReconciliationStatus.ScanFailed, failed.Status);
            Assert.Contains("503", failed.Error, StringComparison.Ordinal);
            Assert.Equal(2, failed.FindingCount); // the receipt on a transfer in review, the webhook without a transfer
        }
        Assert.Single(await Emails());

        // 6. The monitor: an hour without SePay, a burst of bad signatures, a failed and a stuck transfer, a conflict.
        clock.Now = Local(22, 6, 30);
        var failing = Api("failing", 120_000, "LOI", $"RF{marker}", Local(22, 5));
        var stuck = Api("stuck", 130_000, "CHO", $"RS{marker}", Local(22, 6));
        await using (var db = new FinanceDbContext(options))
        {
            var failedRow = BankTransaction.Receive(config, failing, FetchedBy.Doorbell, Local(22, 5));
            db.BankTransactions.AddRange(failedRow, BankTransaction.Receive(config, stuck, FetchedBy.Doorbell, Local(22, 6, 10)));
            await db.SaveChangesAsync();
        }
        await Sql($"""
            UPDATE finance.bank_transactions SET status = 'Failed', attempt_count = 5,
                last_error = 'TuitionPersistenceException: x → PostgresException: 23505: duplicate key value violates unique constraint "ux_receipts_bank_transaction_live"'
            WHERE provider_txn_id = '{failing.ProviderTxnId}';
            UPDATE finance.payment_provider_configs SET last_pull_error = 'SePay answered 503.', last_pulled_at_utc = '{Local(22, 5):O}',
                invalid_signature_count = 6, last_invalid_signature_at_utc = '{Local(22, 6, 25):O}'
            WHERE id = @config;
            """);
        await Monitor();
        alerts = await Alerts();
        Assert.Equal(PaymentAlertState.Open, alerts.Single(x => x.Kind == AlertKinds.ApiDown).State);
        Assert.Equal(6, alerts.Single(x => x.Kind == AlertKinds.BadSignatures).Metric);
        Assert.Single(alerts, x => x.Kind == AlertKinds.TransferFailed);
        Assert.Single(alerts, x => x.Kind == AlertKinds.ReceiptConflict);
        Assert.Single(alerts, x => x.Kind == AlertKinds.StuckReceived && x.Detail.Contains("2 giao dịch", StringComparison.Ordinal)); // with the missed one, never booked here
        var emails = await Emails();
        Assert.Equal(2, emails.Count);
        Assert.Contains("5 vấn đề mới", emails[1].Subject, StringComparison.Ordinal);

        // Seen again a minute later: nothing new. SePay answers again: that alert closes, the rest stay.
        clock.Now = Local(22, 6, 31);
        await Sql("UPDATE finance.payment_provider_configs SET invalid_signature_count = 8 WHERE id = @config");
        await Monitor();
        Assert.Equal(2, (await Emails()).Count);
        await Sql($"UPDATE finance.payment_provider_configs SET last_pull_error = NULL, last_pulled_at_utc = '{Local(22, 6, 31):O}' WHERE id = @config");
        await Monitor();
        alerts = await Alerts();
        Assert.Equal(PaymentAlertState.Resolved, alerts.Single(x => x.Kind == AlertKinds.ApiDown).State);
        Assert.Equal(PaymentAlertState.Open, alerts.Single(x => x.Kind == AlertKinds.BadSignatures).State);
        Assert.Equal(6, alerts.Single(x => x.Kind == AlertKinds.BadSignatures).Metric);

        // SePay fails again later: the same alert opens again and is emailed again.
        clock.Now = Local(22, 6, 50);
        await Sql($"UPDATE finance.payment_provider_configs SET last_pull_error = 'SePay answered 500.', last_pulled_at_utc = '{Local(22, 5, 40):O}' WHERE id = @config");
        await Monitor();
        Assert.Equal(PaymentAlertState.Open, (await Alerts()).Single(x => x.Kind == AlertKinds.ApiDown).State);
        emails = await Emails();
        Assert.Equal(3, emails.Count);
        Assert.Contains("Không lấy được giao dịch từ SePay", emails[2].Subject, StringComparison.Ordinal);

        // 7. 07:00: one digest with what is still open, once a day.
        await Sql("UPDATE finance.bank_transactions SET first_seen_at_utc = first_seen_at_utc - interval '3 days' WHERE center_id = @center AND status = 'NeedsReview'");
        clock.Now = Local(22, 7, 1);
        await Monitor();
        await Monitor();
        emails = await Emails();
        Assert.Equal(4, emails.Count);
        Assert.Equal("Tổng hợp thanh toán tự động", emails[3].Subject);
        Assert.Contains("1 khoản chưa rõ của ai", emails[3].Body, StringComparison.Ordinal);
        Assert.Contains("Nhiều webhook sai chữ ký", emails[3].Body, StringComparison.Ordinal);
        Assert.DoesNotContain("SePay đổi dữ liệu", emails[3].Body, StringComparison.Ordinal); // acknowledged
        clock.Now = Local(23, 7, 1);
        await Monitor();
        Assert.Equal(5, (await Emails()).Count);

        // 8. Metrics for the week, from what is stored.
        await using (var db = new FinanceDbContext(options))
        {
            var workflow = new PaymentOpsWorkflow(Ops(db), Ops(db), clock);
            Assert.Equal(PaymentOpsErrors.InvalidDays, (await workflow.MetricsAsync(center.Id, 0, default)).Error);
            var metrics = (await workflow.MetricsAsync(center.Id, 7, default)).Value;
            Assert.Equal(3, metrics.Doorbells);
            Assert.Equal(0, metrics.DoorbellsRedelivered);
            Assert.Equal(24, metrics.DoorbellsByHour.Count);
            Assert.Equal(0, metrics.DoorbellsByHour.Sum()); // all three are older than a day
            Assert.Equal(8, metrics.InvalidSignatureTotal);
            Assert.Equal(7, metrics.Transfers);
            Assert.Equal(6, metrics.ByFetchedBy["Doorbell"]);
            Assert.Equal(1, metrics.ByFetchedBy["Manual"]);
            Assert.Equal(3, metrics.ByStatus["Booked"]);
            Assert.Equal(1, metrics.ByStatus["Failed"]);
            Assert.Equal(1, metrics.ReceiptConflicts);
            Assert.True(metrics.SeenAgain >= 4); // listed again by the second and third reconciliation
            Assert.NotNull(metrics.LatencyP50Seconds);
            Assert.Equal((clock.Now - Local(20, 2, 5)).TotalSeconds, metrics.OldestReceivedSeconds!.Value, 3);
            Assert.True(metrics.OpenAlerts > 0);
            Assert.Equal("ScanFailed", metrics.LastReconciliationStatus);
        }
    }
}
