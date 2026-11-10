using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Infrastructure.Payments;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 2e: the SePay v2 client against a scripted HTTP server.</summary>
public sealed class SepayBankFeedTests
{
    private const string Page = """
        {"data":[
          {"id":"254cd3a9-d7b0-11f1-8895-0afffe87053b","transaction_date":"2026-11-04 00:58:16","account_number":"0000000001",
           "va":"SBSEPAYFIVGLYBPNY0F","transfer_type":"in","amount_in":100000,"amount_out":0,"accumulated":0,
           "transaction_content":"HPFGXXVAPYP HOC PHI","reference_number":"SB2DD5A65074EA","code":"",
           "bank_brand_name":"Vietcombank","bank_account_id":"acc-1","va_id":"va-1","webhook_success":0},
          {"id":"out-1","transaction_date":"2026-11-03 10:00:00","account_number":"0000000001","transfer_type":"out",
           "amount_in":0,"amount_out":50000,"transaction_content":"Phi","reference_number":"","bank_brand_name":"Vietcombank"}
        ],"meta":{"pagination":{"total":2,"per_page":100,"current_page":1,"last_page":1,"has_more":true}}}
        """;

    private sealed class Scripted(HttpStatusCode status, string body, TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (retryAfter is { } wait) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(wait);
            return Task.FromResult(response);
        }
    }

    private static (SepayBankFeed Feed, Scripted Handler) Feed(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        var handler = new Scripted(status, body, retryAfter);
        return (new SepayBankFeed(new HttpClient(handler), new SepayOptions
        {
            ApiBaseUrl = new Uri("https://userapi-sandbox.sepay.vn/v2/")
        }), handler);
    }

    [Fact]
    public async Task A_page_is_read_with_both_directions_vietnam_time_and_the_raw_item()
    {
        var (feed, handler) = Feed(HttpStatusCode.OK, Page);
        var page = await feed.ListAsync("token-1", new BankFeedQuery("since-1", null, 2, 100), default);

        Assert.True(page.HasMore);
        Assert.Equal(2, page.Transactions.Count);
        var first = page.Transactions[0];
        Assert.Equal("254cd3a9-d7b0-11f1-8895-0afffe87053b", first.ProviderTxnId);
        Assert.Equal(TransferDirection.In, first.Direction);
        Assert.Equal(100_000, first.Amount);
        Assert.Equal("Vietcombank", first.BankCode);
        Assert.Equal("SBSEPAYFIVGLYBPNY0F", first.VirtualAccount);
        Assert.Equal("SB2DD5A65074EA", first.ReferenceCode);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 17, 58, 16, TimeSpan.Zero), first.TransactedAtUtc);
        Assert.Contains("\"webhook_success\":0", first.RawPayload, StringComparison.Ordinal);
        Assert.Equal(TransferDirection.Out, page.Transactions[1].Direction);

        var request = handler.Last!;
        Assert.Equal("https://userapi-sandbox.sepay.vn/v2/transactions?per_page=100&page=2&since_id=since-1",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("token-1", request.Headers.Authorization.Parameter);
        Assert.Contains(SepayBankFeed.UserAgent, request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_pull_filters_by_vietnam_date_instead_of_a_cursor()
    {
        var (feed, handler) = Feed(HttpStatusCode.OK, """{"data":[]}""");
        var page = await feed.ListAsync("t", new BankFeedQuery(null, new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero), 1, 100), default);
        Assert.Empty(page.Transactions);
        Assert.False(page.HasMore);
        Assert.EndsWith("transaction_date_from=2026-11-10%2009%3A00%3A00", handler.Last!.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    public async Task Errors_say_whether_to_retry(HttpStatusCode status, bool retryable)
    {
        var (feed, _) = Feed(status, "{}", status == HttpStatusCode.TooManyRequests ? TimeSpan.FromSeconds(2) : null);
        var error = await Assert.ThrowsAsync<BankFeedException>(() => feed.ListAsync("t", new BankFeedQuery(null, null, 1, 100), default));
        Assert.Equal(retryable, error.Retryable);
        if (status == HttpStatusCode.TooManyRequests) Assert.Equal(TimeSpan.FromSeconds(2), error.RetryAfter);
    }

    [Theory]
    [InlineData("""{"data":[{"transaction_date":"2026-11-04 00:58:16","account_number":"1","transfer_type":"in","amount_in":1}]}""")]
    [InlineData("""{"data":[{"id":"x","transaction_date":"2026-11-04 00:58:16","account_number":"1","transfer_type":"sideways","amount_in":1}]}""")]
    [InlineData("""{"data":[{"id":"x","transaction_date":"04/11/2026","account_number":"1","transfer_type":"in","amount_in":1}]}""")]
    [InlineData("""{"data":[{"id":"x","transaction_date":"2026-11-04 00:58:16","account_number":"1","transfer_type":"in","amount_in":0}]}""")]
    [InlineData("""{"oops":true}""")]
    public async Task An_unreadable_transaction_stops_the_pull_instead_of_being_skipped(string body)
    {
        var (feed, _) = Feed(HttpStatusCode.OK, body);
        var error = await Assert.ThrowsAsync<BankFeedException>(() => feed.ListAsync("t", new BankFeedQuery(null, null, 1, 100), default));
        Assert.False(error.Retryable);
    }
}

/// <summary>
/// Round 2e against real PostgreSQL with a scripted feed. The clock is in 2001 so configs left by other runs, scheduled in
/// 2026, are never due for this fetcher.
/// </summary>
[Collection(TuitionDatabase.Name)]
public sealed class RealPostgresPaymentFetcherTests
{
    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    /// <summary>Answers each call with the next script entry; records what was asked.</summary>
    private sealed class ScriptedFeed : IBankFeedProvider
    {
        public Queue<Func<BankFeedQuery, BankFeedPage>> Script { get; } = new();
        public List<(string Token, BankFeedQuery Query)> Calls { get; } = [];
        public PaymentProvider Provider => PaymentProvider.Sepay;

        public async Task<BankFeedPage> ListAsync(string apiToken, BankFeedQuery query, CancellationToken token)
        {
            lock (Calls) Calls.Add((apiToken, query));
            await Task.Delay(50, token);
            Func<BankFeedQuery, BankFeedPage> next;
            lock (Script) next = Script.Count > 0 ? Script.Dequeue() : _ => new BankFeedPage([], false);
            return next(query);
        }
    }

    private static ReceivedBankTransaction In(string id, string? reference, DateTimeOffset at, string account, long amount = 100_000) =>
        new(id, "Vietcombank", account, "acc", null, TransferDirection.In, amount, $"HP{id} HOC PHI", reference, at,
            $$"""{"id":"{{id}}"}""");

    [LocalPostgresFact]
    public async Task Pulls_store_each_transaction_once_hold_reused_references_and_follow_the_schedule()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var clock = new Clock(new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var marker = Guid.NewGuid().ToString("N")[..12];
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var center = Guid.NewGuid();
        var token = $"tok-{marker}";
        var config = PaymentProviderConfig.Create(center, PaymentProvider.Sepay, "970436", account,
            PaymentIngestionDomainTests.Secrets(protector, center, token), Guid.NewGuid(), clock.Now);
        config.ChangeMode(PaymentProviderMode.Shadow, Guid.NewGuid(), clock.Now);
        var offCenter = Guid.NewGuid();
        var off = PaymentProviderConfig.Create(offCenter, PaymentProvider.Sepay, "970436", account + "0",
            PaymentIngestionDomainTests.Secrets(protector, offCenter, $"off-{marker}"), Guid.NewGuid(), clock.Now);
        await using (var db = new FinanceDbContext(options))
        {
            db.PaymentProviderConfigs.AddRange(config, off);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE finance.payment_provider_configs SET next_pull_at_utc = {clock.Now.AddYears(-1)} WHERE id = {off.Id}");
        }

        var feed = new ScriptedFeed();
        // Other runs may leave due configs behind; they cannot be decrypted with this test's keys and never reach the feed,
        // so everything below is measured on this config's own calls.
        int Mine() { lock (feed.Calls) return feed.Calls.Count(x => x.Token == token); }
        async Task<int> Run()
        {
            var before = Mine();
            await using var db = new FinanceDbContext(options);
            await new PaymentFetcher(new PaymentPullStore(db), [feed], protector, clock).RunOnceAsync(50, default);
            return Mine() - before;
        }
        async Task<PaymentProviderConfig> Config()
        {
            await using var db = new FinanceDbContext(options);
            return await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == config.Id);
        }
        async Task<List<BankTransaction>> Rows()
        {
            await using var db = new FinanceDbContext(options);
            return await db.BankTransactions.AsNoTracking().Where(x => x.ConfigId == config.Id).OrderBy(x => x.TransactedAtUtc).ToListAsync();
        }

        // ---- first pull: from the connection date, two pages, outgoing ignored, cursor = newest id ----
        var t = clock.Now;
        feed.Script.Enqueue(q => new BankFeedPage([In($"c-{marker}", $"SBC{marker}", t.AddMinutes(-1), account),
            In($"b-{marker}", $"SBB{marker}", t.AddMinutes(-2), account)], true));
        feed.Script.Enqueue(q => new BankFeedPage([In($"a-{marker}", null, t.AddMinutes(-3), "999999") with
            { Direction = TransferDirection.Out }], false));
        Assert.Equal(2, await Run());
        var firstCalls = feed.Calls.Where(x => x.Token == token).ToArray();
        Assert.Null(firstCalls[0].Query.SinceId);
        Assert.Equal(config.CreatedAtUtc, firstCalls[0].Query.FromUtc);
        Assert.Equal(2, firstCalls[1].Query.Page);
        var rows = await Rows();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, x => Assert.Equal(FetchedBy.Periodic, x.FetchedBy));
        Assert.All(rows, x => Assert.Equal("970436", x.BankBin));
        var after = await Config();
        Assert.Equal($"c-{marker}", after.PullCursor);
        Assert.Equal(clock.Now.AddMinutes(5), after.NextPullAtUtc);
        Assert.Null(after.PullProcessingId);

        // ---- not due again for five minutes ----
        Assert.Equal(0, await Run());

        // ---- a doorbell with nothing new yet: retry after 1 s, then 3 s ----
        clock.Now = clock.Now.AddSeconds(2);
        await using (var db = new FinanceDbContext(options))
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE finance.payment_provider_configs SET pull_requested_at_utc = {clock.Now}, next_pull_at_utc = {clock.Now} WHERE id = {config.Id}");
        Assert.Equal(1, await Run());
        Assert.Equal($"c-{marker}", feed.Calls.Last(x => x.Token == token).Query.SinceId);
        after = await Config();
        Assert.Equal((1, clock.Now.AddSeconds(1)), (after.PullRetryCount, after.NextPullAtUtc));
        clock.Now = clock.Now.AddSeconds(1.5);
        Assert.Equal(1, await Run());
        after = await Config();
        Assert.Equal((2, clock.Now.AddSeconds(3)), (after.PullRetryCount, after.NextPullAtUtc));

        // ---- the transaction arrives: a repeat, a new one, and a second provider id for a known bank reference ----
        clock.Now = clock.Now.AddSeconds(4);
        feed.Script.Enqueue(q => new BankFeedPage([
            In($"e-{marker}", $"SBB{marker}", clock.Now.AddSeconds(-1), account),
            In($"d-{marker}", $"SBD{marker}", clock.Now.AddSeconds(-2), account),
            In($"c-{marker}", $"SBC{marker}", t.AddMinutes(-1), account)], false));
        Assert.Equal(1, await Run());
        rows = await Rows();
        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Single(x => x.ProviderTxnId == $"c-{marker}").SeenCount);
        var heldRow = rows.Single(x => x.ProviderTxnId == $"e-{marker}");
        Assert.Equal(BankTransactionStatus.Held, heldRow.Status);
        Assert.Null(heldRow.SemanticKey);
        Assert.Equal(rows.Single(x => x.ProviderTxnId == $"b-{marker}").Id, heldRow.DuplicateOfId);
        Assert.Equal(FetchedBy.Doorbell, rows.Single(x => x.ProviderTxnId == $"d-{marker}").FetchedBy);
        after = await Config();
        Assert.Equal(($"e-{marker}", 0, clock.Now.AddMinutes(5)), (after.PullCursor, after.PullRetryCount, after.NextPullAtUtc));
        await using (var db = new FinanceDbContext(options))
            Assert.Equal("transaction_held", (await db.PaymentAuditEvents.AsNoTracking().SingleAsync(x => x.TargetId == heldRow.Id)).Action);

        // ---- a failing feed records the error, keeps the cursor and tries again in a minute ----
        clock.Now = clock.Now.AddMinutes(6);
        feed.Script.Enqueue(_ => throw new BankFeedException("SePay answered 503.", true));
        Assert.Equal(1, await Run());
        after = await Config();
        Assert.Equal(("SePay answered 503.", $"e-{marker}", clock.Now.AddMinutes(1)), (after.LastPullError, after.PullCursor, after.NextPullAtUtc));
        Assert.Null(after.PullProcessingId);

        // ---- two Workers at once: only one pulls, nothing is stored twice ----
        clock.Now = clock.Now.AddMinutes(2);
        feed.Script.Enqueue(q => new BankFeedPage([In($"f-{marker}", $"SBF{marker}", clock.Now, account)], false));
        var before = Mine();
        await Task.WhenAll(Run(), Run());
        Assert.Equal(1, Mine() - before);
        Assert.Single(await Rows(), x => x.ProviderTxnId == $"f-{marker}");
        Assert.Null((await Config()).LastPullError);

        // ---- a config that is Off is never pulled ----
        Assert.DoesNotContain(feed.Calls, x => x.Token == $"off-{marker}");
    }
}
