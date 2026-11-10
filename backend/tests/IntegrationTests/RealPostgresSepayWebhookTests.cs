using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 2d through the real HTTP pipeline and PostgreSQL: the SePay doorbell.</summary>
public sealed class RealPostgresSepayWebhookTests
{
    private const string Path = "/api/v1/payments/webhooks/sepay/";
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "::1"];

    private static string Sign(string secret, string timestamp, byte[] body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.ASCII.GetBytes(timestamp + ".").Concat(body).ToArray()));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string endpointKey, byte[] body,
        string? signature, string? timestamp, string accept = "text/plain")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path + endpointKey)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new MediaTypeHeaderValue("application/json") } }
        };
        request.Headers.Accept.ParseAdd(accept);
        if (signature is not null) request.Headers.Add("X-SePay-Signature", signature);
        if (timestamp is not null) request.Headers.Add("X-SePay-Timestamp", timestamp);
        return await client.SendAsync(request);
    }

    [LocalPostgresFact]
    public async Task Signed_doorbells_are_logged_once_and_ask_for_a_pull_while_bad_ones_are_only_counted()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Contains(parsed.Host, LocalHosts);

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MainDatabase", connectionString);
            builder.UseSetting("Jwt:Issuer", "TutorManagement.E2E");
            builder.UseSetting("Jwt:Audience", "TutorManagement.E2E");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "1");
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
            builder.UseSetting("Payments:EncryptionKeys:w1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseSetting("Payments:ActiveKeyId", "w1");
            builder.UseSetting("Payments:FingerprintKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var marker = Guid.NewGuid().ToString("N");
        var secret = $"whsec-{marker}";
        PaymentProviderConfig shadow, off;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            ProtectedSecrets Secrets(Guid center, string token) => new(
                protector.Protect(secret, SecretPurposes.WebhookSecret(center, "Sepay")),
                protector.Protect(token, SecretPurposes.ApiToken(center, "Sepay")),
                protector.Fingerprint(token, SecretPurposes.ApiTokenFingerprint("Sepay")), protector.ActiveKeyId);
            var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
            shadow = PaymentProviderConfig.Create(a, PaymentProvider.Sepay, "970436", "111222333", Secrets(a, $"ta-{marker}"),
                Guid.NewGuid(), DateTimeOffset.UtcNow);
            shadow.ChangeMode(PaymentProviderMode.Shadow, Guid.NewGuid(), DateTimeOffset.UtcNow);
            off = PaymentProviderConfig.Create(b, PaymentProvider.Sepay, "970436", "444555666", Secrets(b, $"tb-{marker}"),
                Guid.NewGuid(), DateTimeOffset.UtcNow);
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            db.PaymentProviderConfigs.AddRange(shadow, off);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE finance.payment_provider_configs SET next_pull_at_utc = NULL WHERE id = {shadow.Id}");
        }

        var webhookId = RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue);
        var body = Encoding.UTF8.GetBytes(
            $$"""{"id":{{webhookId}},"gateway":"Vietcombank","transferType":"in","transferAmount":100000,"content":"HPFGXXVAPYP HOC PHI","referenceCode":"SB{{marker[..10]}}","code":""}""");
        string Now(int offset = 0) => (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + offset).ToString(CultureInfo.InvariantCulture);

        // ---- unknown endpoint: 404 with an empty body, nothing stored ----
        using (var unknown = await PostAsync(client, PaymentProviderConfig.NewEndpointKey(), body, Sign(secret, Now(), body), Now()))
        {
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Empty(await unknown.Content.ReadAsStringAsync());
        }

        // ---- a signed doorbell: fixed JSON body even when the client accepts text only; asks for a pull ----
        var ts = Now();
        using (var ok = await PostAsync(client, shadow.EndpointKey, body, Sign(secret, ts, body), ts))
        {
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("{\"success\":true}", await ok.Content.ReadAsStringAsync());
        }
        // ---- SePay sends the same delivery again: still one row ----
        var again = Now();
        using (var repeat = await PostAsync(client, shadow.EndpointKey, body, Sign(secret, again, body), again))
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        // ---- forged, tampered, stale or unsigned: 401 and only counted ----
        var forged = Encoding.UTF8.GetBytes($$"""{"id":{{webhookId + 1}},"transferAmount":999000000}""");
        var t = Now();
        using (var bad = await PostAsync(client, shadow.EndpointKey, forged, Sign("wrong-secret", t, forged), t))
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        using (var tampered = await PostAsync(client, shadow.EndpointKey, forged, Sign(secret, t, body), t))
            Assert.Equal(HttpStatusCode.Unauthorized, tampered.StatusCode);
        var old = Now(-1000);
        using (var stale = await PostAsync(client, shadow.EndpointKey, forged, Sign(secret, old, forged), old))
            Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        using (var unsigned = await PostAsync(client, shadow.EndpointKey, forged, null, null))
            Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        // ---- a center that is Off still logs the doorbell but is not pulled ----
        var tsOff = Now();
        using (var offOk = await PostAsync(client, off.EndpointKey, body, Sign(secret, tsOff, body), tsOff, "application/json"))
            Assert.Equal(HttpStatusCode.OK, offOk.StatusCode);

        // ---- bodies over 64 KB are refused before the signature is checked ----
        var huge = new byte[SepayWebhookControllerLimit + 1];
        using (var tooBig = await PostAsync(client, shadow.EndpointKey, huge, Sign(secret, t, huge), t))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooBig.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var deliveries = await db.PaymentWebhookDeliveries.AsNoTracking().Where(x => x.ConfigId == shadow.Id).ToListAsync();
            var delivery = Assert.Single(deliveries);
            Assert.Equal((long)webhookId, delivery.WebhookId);
            Assert.Equal(2, delivery.DeliveryCount);
            Assert.Equal($"SB{marker[..10]}", delivery.ReferenceCode);
            Assert.Equal(100_000, delivery.Amount);
            Assert.Equal(Encoding.UTF8.GetString(body), delivery.RawBody);

            var shadowRow = await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == shadow.Id);
            Assert.NotNull(shadowRow.PullRequestedAtUtc);
            Assert.NotNull(shadowRow.NextPullAtUtc);
            Assert.Equal(4, shadowRow.InvalidSignatureCount);
            Assert.Equal(shadow.Version, shadowRow.Version);

            var offRow = await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == off.Id);
            Assert.Null(offRow.PullRequestedAtUtc);
            Assert.Single(await db.PaymentWebhookDeliveries.AsNoTracking().Where(x => x.ConfigId == off.Id).ToListAsync());
        }
    }

    private const int SepayWebhookControllerLimit =
        TutorManagement.Modules.Finance.Api.Payments.SepayWebhookController.MaxBodyBytes;
}
