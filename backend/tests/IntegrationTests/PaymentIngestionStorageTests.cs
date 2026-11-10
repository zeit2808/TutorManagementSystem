using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Security;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 2b on pure values: secret encryption, duplicate keys and the inbox row rules.</summary>
public sealed class PaymentIngestionDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);

    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    internal static AesGcmSecretProtector Protector(string active, string fingerprintKey, params (string Id, string Key)[] keys) =>
        new(new PaymentSecretOptions
        {
            EncryptionKeys = keys.ToDictionary(x => x.Id, x => x.Key),
            ActiveKeyId = active,
            FingerprintKey = fingerprintKey
        });

    internal static ProtectedSecrets Secrets(ISecretProtector protector, Guid centerId, string token) => new(
        protector.Protect("webhook-secret", SecretPurposes.WebhookSecret(centerId, "Sepay")),
        protector.Protect(token, SecretPurposes.ApiToken(centerId, "Sepay")),
        protector.Fingerprint(token, SecretPurposes.ApiTokenFingerprint("Sepay")),
        protector.ActiveKeyId);

    internal static ReceivedBankTransaction Transfer(string id, string? reference, string account = "0123456789",
        string content = "HPFGXXVAPYP HOC PHI", string raw = "{\"a\":1}") =>
        new(id, "Vietcombank", account, "acc-1", "VQRQAABBCC", TransferDirection.In, 100_000, content, reference,
            Now.AddMinutes(-5), raw);

    [Fact]
    public void Secrets_round_trip_and_reject_tampering_or_another_purpose()
    {
        var protector = Protector("k1", Key(), ("k1", Key()));
        var center = Guid.NewGuid();
        var purpose = SecretPurposes.ApiToken(center, "Sepay");
        var sealedValue = protector.Protect("token-123", purpose);

        Assert.Equal("token-123", protector.Unprotect(sealedValue, "k1", purpose));
        Assert.NotEqual(sealedValue, protector.Protect("token-123", purpose));
        Assert.DoesNotContain("token-123", System.Text.Encoding.UTF8.GetString(sealedValue), StringComparison.Ordinal);

        var tampered = (byte[])sealedValue.Clone();
        tampered[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered, "k1", purpose));
        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect(sealedValue, "k1", SecretPurposes.WebhookSecret(center, "Sepay")));
        Assert.ThrowsAny<CryptographicException>(() =>
            protector.Unprotect(sealedValue, "k1", SecretPurposes.ApiToken(Guid.NewGuid(), "Sepay")));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(sealedValue, "k9", purpose));
    }

    [Fact]
    public void Rotating_keys_keeps_old_values_readable_and_fingerprints_unchanged()
    {
        var (k1, k2, fingerprint) = (Key(), Key(), Key());
        var before = Protector("k1", fingerprint, ("k1", k1));
        var after = Protector("k2", fingerprint, ("k1", k1), ("k2", k2));
        var purpose = SecretPurposes.ApiToken(Guid.NewGuid(), "Sepay");
        var old = before.Protect("token-123", purpose);

        Assert.Equal("token-123", after.Unprotect(old, "k1", purpose));
        Assert.Equal("k2", after.ActiveKeyId);
        Assert.Equal(before.Fingerprint("token-123", "fp"), after.Fingerprint("token-123", "fp"));
        Assert.NotEqual(after.Fingerprint("token-123", "fp"), after.Fingerprint("token-124", "fp"));
        Assert.NotEqual(after.Fingerprint("token-123", "fp"), after.Fingerprint("token-123", "other"));
    }

    [Fact]
    public void Missing_or_short_keys_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => Protector("k1", Key()));
        Assert.Throws<InvalidOperationException>(() => Protector("k2", Key(), ("k1", Key())));
        Assert.Throws<InvalidOperationException>(() =>
            Protector("k1", Key(), ("k1", Convert.ToBase64String(new byte[16]))));
        Assert.Throws<InvalidOperationException>(() => Protector("k1", "not base64!", ("k1", Key())));
        Assert.Throws<InvalidOperationException>(() =>
            Protector("k1", Convert.ToBase64String(new byte[8]), ("k1", Key())));
    }

    [Fact]
    public void The_bank_reference_key_is_normalized_and_absent_without_a_reference()
    {
        Assert.Equal("VIETCOMBANK|0123456789|SB2DD5A65074EA",
            BankTransactionKeys.SemanticKey(" Vietcombank ", "0123 456789", "sb2dd5a65074ea"));
        Assert.Null(BankTransactionKeys.SemanticKey("Vietcombank", "0123456789", null));
        Assert.Null(BankTransactionKeys.SemanticKey("Vietcombank", "0123456789", "   "));
    }

    [Fact]
    public void The_checksum_follows_business_fields_not_the_raw_json()
    {
        var a = Transfer("t1", "SB1");
        Assert.Equal(BankTransactionKeys.Checksum(a), BankTransactionKeys.Checksum(a with { RawPayload = "{\"b\":2,\"a\":1}" }));
        Assert.NotEqual(BankTransactionKeys.Checksum(a), BankTransactionKeys.Checksum(a with { Content = "HPX HOC PHI" }));
        Assert.NotEqual(BankTransactionKeys.Checksum(a), BankTransactionKeys.Checksum(a with { Amount = 100_001 }));
    }

    [Fact]
    public void Only_incoming_transfers_are_stored_and_a_duplicate_reference_is_held()
    {
        var protector = Protector("k1", Key(), ("k1", Key()));
        var center = Guid.NewGuid();
        var config = PaymentProviderConfig.Create(center, PaymentProvider.Sepay, "970436", "0123456789",
            Secrets(protector, center, "token"), Guid.NewGuid(), Now);

        var row = BankTransaction.Receive(config, Transfer("t1", "SB1"), FetchedBy.Doorbell, Now);
        Assert.Equal(BankTransactionStatus.Received, row.Status);
        Assert.Equal("970436", row.BankBin);
        Assert.Equal("VIETCOMBANK|0123456789|SB1", row.SemanticKey);
        Assert.Equal(1, row.SeenCount);

        var viaVirtual = BankTransaction.Receive(config, Transfer("t2", null, account: "9999") with { VirtualAccount = "0123456789" },
            FetchedBy.Periodic, Now);
        Assert.Equal("970436", viaVirtual.BankBin);
        Assert.Null(viaVirtual.SemanticKey);
        Assert.Null(BankTransaction.Receive(config, Transfer("t3", null, account: "5555"), FetchedBy.Periodic, Now).BankBin);

        var held = BankTransaction.HoldAsDuplicate(config, Transfer("t4", "SB1"), row.Id, FetchedBy.Nightly, Now);
        Assert.Equal(BankTransactionStatus.Held, held.Status);
        Assert.Null(held.SemanticKey);
        Assert.Equal(row.Id, held.DuplicateOfId);

        Assert.Throws<ArgumentException>(() => BankTransaction.Receive(config,
            Transfer("t5", "SB5") with { Direction = TransferDirection.Out }, FetchedBy.Doorbell, Now));
        Assert.Throws<ArgumentException>(() => BankTransaction.Receive(config,
            Transfer("t6", "SB6") with { Amount = 0 }, FetchedBy.Doorbell, Now));
    }

    [Fact]
    public void Switching_to_auto_records_the_cutoff_and_off_ignores_doorbells()
    {
        var protector = Protector("k1", Key(), ("k1", Key()));
        var center = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var config = PaymentProviderConfig.Create(center, PaymentProvider.Sepay, "970436", "0123456789",
            Secrets(protector, center, "token"), actor, Now);
        Assert.Equal(PaymentProviderConfig.EndpointKeyLength, config.EndpointKey.Length);
        Assert.Equal(PaymentProviderMode.Off, config.Mode);

        config.RequestPull(Now);
        Assert.Null(config.NextPullAtUtc);

        config.ChangeMode(PaymentProviderMode.Shadow, actor, Now.AddMinutes(1));
        Assert.Null(config.AutoSinceUtc);
        config.RequestPull(Now.AddMinutes(2));
        Assert.Equal(Now.AddMinutes(2), config.NextPullAtUtc);

        config.ChangeMode(PaymentProviderMode.Auto, actor, Now.AddMinutes(3));
        Assert.Equal(Now.AddMinutes(3), config.AutoSinceUtc);
        Assert.Equal(2, config.Version);
        Assert.Throws<ArgumentException>(() => PaymentProviderConfig.Create(center, PaymentProvider.Sepay, "97043", "0123456789",
            Secrets(protector, center, "token"), actor, Now));
    }
}

/// <summary>Round 2b against real PostgreSQL: the database itself refuses every kind of duplicate.</summary>
public sealed class RealPostgresPaymentIngestionStorageTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);

    private static string ConnectionString() =>
        Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
        new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
            .GetConnectionString("MainDatabase") ??
        throw new InvalidOperationException("Configure a local test database.");

    private static async Task<string?> UniqueViolation(DbContextOptions<FinanceDbContext> options, Action<FinanceDbContext> add)
    {
        await using var db = new FinanceDbContext(options);
        add(db);
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            return pg.ConstraintName;
        }
    }

    [LocalPostgresFact]
    public async Task Duplicate_transactions_tokens_accounts_and_webhooks_are_refused_by_the_database()
    {
        var options = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(ConnectionString()).Options;
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        var marker = Guid.NewGuid().ToString("N");
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var (centerA, centerB, centerC, actor) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var data = new PaymentTestData(ConnectionString());
        data.Center(centerA);
        data.Center(centerB);
        data.Center(centerC);
        var configA = PaymentProviderConfig.Create(centerA, PaymentProvider.Sepay, "970436", account,
            PaymentIngestionDomainTests.Secrets(protector, centerA, $"token-a-{marker}"), actor, Now);
        var configB = PaymentProviderConfig.Create(centerB, PaymentProvider.Sepay, "970436", account,
            PaymentIngestionDomainTests.Secrets(protector, centerB, $"token-b-{marker}"), actor, Now);

        await using (var db = new FinanceDbContext(options))
        {
            db.PaymentProviderConfigs.AddRange(configA, configB);
            await db.SaveChangesAsync();
        }

        // ---- the same SePay token cannot be connected to a second center ----
        Assert.Equal("ux_payment_provider_configs_token", await UniqueViolation(options, db =>
            db.PaymentProviderConfigs.Add(PaymentProviderConfig.Create(centerC, PaymentProvider.Sepay, "970436", "111",
                PaymentIngestionDomainTests.Secrets(protector, centerC, $"token-a-{marker}"), actor, Now))));

        // ---- one bank account books automatically in one center only ----
        await using (var db = new FinanceDbContext(options))
        {
            var a = await db.PaymentProviderConfigs.SingleAsync(x => x.Id == configA.Id);
            a.ChangeMode(PaymentProviderMode.Auto, actor, Now);
            await db.SaveChangesAsync();
        }
        Assert.Equal("ux_payment_provider_configs_auto_account", await UniqueViolation(options, db =>
        {
            var b = db.PaymentProviderConfigs.Single(x => x.Id == configB.Id);
            b.ChangeMode(PaymentProviderMode.Auto, actor, Now);
        }));

        // ---- key 1: one row per provider transaction id, even from another config ----
        var reference = $"SB{marker[..12]}";
        var first = BankTransaction.Receive(configA, PaymentIngestionDomainTests.Transfer($"txn-{marker}", reference, account),
            FetchedBy.Doorbell, Now);
        await using (var db = new FinanceDbContext(options))
        {
            db.BankTransactions.Add(first);
            await db.SaveChangesAsync();
        }
        Assert.Equal("ux_bank_transactions_provider_txn", await UniqueViolation(options, db =>
            db.BankTransactions.Add(BankTransaction.Receive(configB,
                PaymentIngestionDomainTests.Transfer($"txn-{marker}", null, account), FetchedBy.Periodic, Now))));

        // ---- key 2: a second provider id for the same bank reference is refused, but can be held ----
        Assert.Equal("ux_bank_transactions_semantic_key", await UniqueViolation(options, db =>
            db.BankTransactions.Add(BankTransaction.Receive(configA,
                PaymentIngestionDomainTests.Transfer($"txn2-{marker}", reference, account), FetchedBy.Nightly, Now))));
        Assert.Null(await UniqueViolation(options, db =>
            db.BankTransactions.Add(BankTransaction.HoldAsDuplicate(configA,
                PaymentIngestionDomainTests.Transfer($"txn2-{marker}", reference, account), first.Id, FetchedBy.Nightly, Now))));

        // ---- transfers without a bank reference only rely on key 1 ----
        Assert.Null(await UniqueViolation(options, db => db.BankTransactions.AddRange(
            BankTransaction.Receive(configA, PaymentIngestionDomainTests.Transfer($"txn3-{marker}", null, account), FetchedBy.Periodic, Now),
            BankTransaction.Receive(configA, PaymentIngestionDomainTests.Transfer($"txn4-{marker}", null, account), FetchedBy.Periodic, Now))));

        // ---- a webhook id is stored once per config ----
        Assert.Null(await UniqueViolation(options, db =>
            db.PaymentWebhookDeliveries.Add(PaymentWebhookDelivery.Create(configA.Id, 44412, reference, 100_000, "{}", Now))));
        Assert.Equal("ux_payment_webhook_deliveries_webhook", await UniqueViolation(options, db =>
            db.PaymentWebhookDeliveries.Add(PaymentWebhookDelivery.Create(configA.Id, 44412, reference, 100_000, "{}", Now))));

        // ---- secrets read back through the database still decrypt; the amount check holds ----
        await using (var db = new FinanceDbContext(options))
        {
            var stored = await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.Id == configA.Id);
            Assert.Equal($"token-a-{marker}", protector.Unprotect(stored.ApiToken, stored.KeyId,
                SecretPurposes.ApiToken(centerA, "Sepay")));
            Assert.Equal(PaymentProviderMode.Auto, stored.Mode);
            var rows = await db.BankTransactions.AsNoTracking().Where(x => x.ConfigId == configA.Id).ToListAsync();
            Assert.Equal(4, rows.Count);
            Assert.Single(rows, x => x.Status == BankTransactionStatus.Held);
        }
    }

    [LocalPostgresFact]
    public async Task The_member_directory_lists_active_owners_for_alerts()
    {
        var connectionString = ConnectionString();
        var marker = Guid.NewGuid().ToString("N")[..8];
        var past = Now.AddDays(-30);
        var owner = User.CreateExternal($"owner-{marker}@example.invalid", past);
        var staff = User.CreateExternal($"staff-{marker}@example.invalid", past);
        var center = Center.Create("Alert LLC", $"Alert {marker}",
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"alert-{marker}@example.invalid", "0900000000", past);
        center.Approve(owner.Id, past);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;

        await using (var organization = new OrganizationDbContext(
                         new DbContextOptionsBuilder<OrganizationDbContext>().UseNpgsql(connectionString).Options))
        {
            organization.Centers.Add(center);
            await organization.SaveChangesAsync();
        }
        await using (var identity = new IdentityDbContext(identityOptions))
        {
            identity.Users.AddRange(owner, staff);
            identity.CenterMemberships.AddRange(CenterMembership.CreateOwner(center.Id, owner.Id, past),
                CenterMembership.Create(center.Id, staff.Id, CenterMemberRole.Staff, past));
            await identity.SaveChangesAsync();
        }

        await using (var identity = new IdentityDbContext(identityOptions))
        {
            var directory = new CenterMemberDirectory(identity);
            Assert.Equal([owner.Id], await directory.ListActiveUserIdsAsync(center.Id, ["CenterOwner"], default));
            Assert.Equal(new[] { owner.Id, staff.Id }.ToHashSet(),
                (await directory.ListActiveUserIdsAsync(center.Id, ["CenterOwner", "Staff"], default)).ToHashSet());
            Assert.Empty(await directory.ListActiveUserIdsAsync(center.Id, [], default));
            await Assert.ThrowsAsync<ArgumentException>(() => directory.ListActiveUserIdsAsync(center.Id, ["Boss"], default));
        }
    }
}
