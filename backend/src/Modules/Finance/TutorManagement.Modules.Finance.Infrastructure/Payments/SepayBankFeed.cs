using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;

namespace TutorManagement.Modules.Finance.Infrastructure.Payments;

/// <summary><c>Sepay:ApiBaseUrl</c> (production by default; the sandbox is https://userapi-sandbox.sepay.vn/v2/).</summary>
public sealed class SepayOptions
{
    public const string Production = "https://userapi.sepay.vn/v2/";

    public Uri ApiBaseUrl { get; init; } = new(Production);

    public static SepayOptions From(IConfiguration configuration)
    {
        var configured = configuration["Sepay:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(configured)) return new SepayOptions();
        if (!Uri.TryCreate(configured.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Sepay:ApiBaseUrl must be an absolute https URL.");
        return new SepayOptions { ApiBaseUrl = uri };
    }
}

/// <summary>
/// SePay API v2 <c>GET /transactions</c> (measured on the sandbox, docs/THANH-TOAN-QR-KE-HOACH.md section 2):
/// newest first, <c>since_id</c> returns newer rows only, amounts are integers, dates are Vietnam time.
/// Cloudflare refuses clients without a clear User-Agent, so one is always sent.
/// </summary>
public sealed class SepayBankFeed(HttpClient http, SepayOptions options) : IBankFeedProvider
{
    public const string UserAgent = "TutorManagement/1.0";
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    /// <summary>Every field SePay API v2 sent on the sandbox (10/11/2026). A new one is reported to the owner once.</summary>
    internal static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "id", "bank_brand_name", "account_number", "transaction_date", "amount_out", "amount_in", "accumulated",
        "transaction_content", "reference_number", "code", "bank_account_id", "va", "va_id", "transfer_type",
        "webhook_success"
    };

    public PaymentProvider Provider => PaymentProvider.Sepay;

    public async Task<BankFeedPage> ListAsync(string apiToken, BankFeedQuery query, CancellationToken token)
    {
        var parameters = new List<string>
        {
            $"per_page={query.PerPage.ToString(CultureInfo.InvariantCulture)}",
            $"page={query.Page.ToString(CultureInfo.InvariantCulture)}"
        };
        if (!string.IsNullOrEmpty(query.SinceId)) parameters.Add($"since_id={Uri.EscapeDataString(query.SinceId)}");
        else if (query.FromUtc is { } from)
            parameters.Add("transaction_date_from=" + Uri.EscapeDataString(
                TimeZoneInfo.ConvertTime(from, Vietnam).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(options.ApiBaseUrl, "transactions?" + string.Join('&', parameters)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);

        HttpResponseMessage response;
        try { response = await http.SendAsync(request, token); }
        catch (HttpRequestException error) { throw new BankFeedException("SePay could not be reached.", true, inner: error); }
        catch (TaskCanceledException error) when (!token.IsCancellationRequested)
        { throw new BankFeedException("SePay did not answer in time.", true, inner: error); }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new BankFeedException($"SePay refused the API token ({(int)response.StatusCode}).", false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new BankFeedException("SePay rate limit reached.", true, response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(1));
            if ((int)response.StatusCode >= 500)
                throw new BankFeedException($"SePay answered {(int)response.StatusCode}.", true);
            if (!response.IsSuccessStatusCode)
                throw new BankFeedException($"SePay answered {(int)response.StatusCode}.", false);

            var body = await response.Content.ReadAsStringAsync(token);
            return Parse(body);
        }
    }

    internal static BankFeedPage Parse(string body)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(body); }
        catch (JsonException error) { throw new BankFeedException("SePay answered with unreadable JSON.", false, inner: error); }
        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
                throw new BankFeedException("SePay answered without a data list.", false);
            var items = data.EnumerateArray().Select(Transaction).ToArray();
            var unknown = data.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                .SelectMany(x => x.EnumerateObject().Select(p => p.Name)).Where(x => !KnownFields.Contains(x))
                .Distinct(StringComparer.Ordinal).ToArray();
            var hasMore = root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object &&
                          meta.TryGetProperty("pagination", out var page) && page.ValueKind == JsonValueKind.Object &&
                          page.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True;
            return new BankFeedPage(items, hasMore, unknown);
        }
    }

    /// <summary>An item that cannot be read stops the pull instead of being skipped: skipping could lose money.</summary>
    private static ReceivedBankTransaction Transaction(JsonElement item)
    {
        var id = Text(item, "id");
        if (string.IsNullOrWhiteSpace(id)) throw new BankFeedException("A SePay transaction has no id.", false);
        var amountIn = Number(item, "amount_in");
        var amountOut = Number(item, "amount_out");
        var direction = Text(item, "transfer_type") switch
        {
            "in" => TransferDirection.In,
            "out" => TransferDirection.Out,
            null when amountIn > 0 && amountOut == 0 => TransferDirection.In,
            null when amountOut > 0 && amountIn == 0 => TransferDirection.Out,
            var other => throw new BankFeedException($"SePay transaction {id} has an unknown direction '{other}'.", false)
        };
        var amount = direction == TransferDirection.In ? amountIn : amountOut;
        if (amount is null or <= 0) throw new BankFeedException($"SePay transaction {id} has no positive amount.", false);
        var date = Text(item, "transaction_date");
        if (!DateTime.TryParseExact(date, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new BankFeedException($"SePay transaction {id} has an unreadable date.", false);
        var account = Text(item, "account_number");
        if (string.IsNullOrWhiteSpace(account)) throw new BankFeedException($"SePay transaction {id} has no account number.", false);

        return new ReceivedBankTransaction(
            id,
            Text(item, "bank_brand_name") ?? "unknown",
            account,
            Text(item, "bank_account_id"),
            Text(item, "va"),
            direction,
            amount.Value,
            Text(item, "transaction_content") ?? string.Empty,
            Text(item, "reference_number"),
            new DateTimeOffset(local, Vietnam.GetUtcOffset(local)).ToUniversalTime(),
            item.GetRawText());
    }

    private static string? Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        } : null;

    private static long? Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole)) return whole;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var exact) && exact == decimal.Truncate(exact))
            return (long)exact;
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) &&
            parsed == decimal.Truncate(parsed)) return (long)parsed;
        return null;
    }
}
