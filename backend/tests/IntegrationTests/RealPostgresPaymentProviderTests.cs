using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Infrastructure;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Domain.Tuition;
using TutorManagement.Modules.Finance.Infrastructure;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Infrastructure;
using TutorManagement.Modules.Organizations.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>Round 2c against real PostgreSQL: the owner connects SePay with the password asked again.</summary>
public sealed class RealPostgresPaymentProviderTests
{
    private const string Password = "OwnerStrongPass!2026";
    private static readonly RequestOrigin Origin = new("127.0.0.1", "tests", "trace");

    [LocalPostgresFact]
    public async Task The_owner_connects_sepay_with_the_password_and_every_change_is_audited_and_emailed()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
        var past = now.AddDays(-30);
        var marker = Guid.NewGuid().ToString("N")[..10];
        await using var provider = BuildServices(connectionString, now);
        var hasher = provider.GetRequiredService<IPasswordService>();
        var owner = User.Create($"owner{marker}", $"owner-{marker}@example.invalid",
            "09" + RandomNumberGenerator.GetInt32(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture),
            hasher.Hash(Password), past);
        owner.VerifyEmail(past);
        var googleOwner = User.CreateExternal($"google-{marker}@example.invalid", past);
        var center = NewCenter($"Pay {marker}", past, owner.Id);
        var otherCenter = NewCenter($"Pay2 {marker}", past, googleOwner.Id);
        await using var data = new PaymentTestData(connectionString);
        data.Center(center.Id);
        data.Center(otherCenter.Id);
        data.Email(owner.Email);
        data.Email(googleOwner.Email);
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var token1 = $"tok1{marker}{marker}";
        var token2 = $"tok2{marker}{marker}";
        var secret = $"sec{marker}{marker}";

        await using (var scope = provider.CreateAsyncScope())
        {
            var organization = scope.ServiceProvider.GetRequiredService<OrganizationDbContext>();
            organization.Centers.AddRange(center, otherCenter);
            await organization.SaveChangesAsync();
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.AddRange(owner, googleOwner);
            identity.CenterMemberships.AddRange(CenterMembership.CreateOwner(center.Id, owner.Id, past),
                CenterMembership.CreateOwner(otherCenter.Id, googleOwner.Id, past));
            await identity.SaveChangesAsync();
        }

        async Task<T> Run<T>(Func<PaymentProviderWorkflow, Task<T>> action)
        {
            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<PaymentProviderWorkflow>());
        }
        Task<TutorManagement.SharedKernel.Primitives.Result<PaymentProviderDto>> Save(Guid centerId, Guid actor, string password,
            string mode, string? webhookSecret, string? apiToken, int version) =>
            Run(x => x.SaveAsync(centerId, actor, password, mode, webhookSecret, apiToken, version, Origin, default));

        // ---- nothing connected yet; the tuition bank account is required first ----
        Assert.False((await Run(x => x.GetAsync(center.Id, default))).Configured);
        Assert.Equal(TuitionErrors.BankNotConfigured, (await Save(center.Id, owner.Id, Password, "Shadow", secret, token1, 0)).Error);
        await using (var scope = provider.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var settings = FinanceSettings.Default(center.Id);
            settings.Update("970436", account, "TRUNG TAM PAY", 7, true, owner.Id, past);
            finance.FinanceSettings.Add(settings);
            await finance.SaveChangesAsync();
        }

        // ---- refusals happen before anything is written ----
        Assert.Equal(PaymentErrors.InvalidPassword, (await Save(center.Id, owner.Id, "wrong-password", "Shadow", secret, token1, 0)).Error);
        Assert.Equal(PaymentErrors.PasswordRequired,
            (await Save(otherCenter.Id, googleOwner.Id, "anything", "Shadow", secret, token2, 0)).Error);
        Assert.Equal(PaymentErrors.AutoNotAvailable, (await Save(center.Id, owner.Id, Password, "Auto", secret, token1, 0)).Error);
        Assert.Equal(PaymentErrors.InvalidMode, (await Save(center.Id, owner.Id, Password, "Off,Shadow", secret, token1, 0)).Error);
        Assert.Equal(PaymentErrors.InvalidApiToken, (await Save(center.Id, owner.Id, Password, "Shadow", secret, "short", 0)).Error);
        Assert.Equal(PaymentErrors.SecretsRequired, (await Save(center.Id, owner.Id, Password, "Shadow", null, token1, 0)).Error);
        Assert.False((await Run(x => x.GetAsync(center.Id, default))).Configured);

        // ---- first connection: Shadow, secrets stored encrypted, owner emailed, audit written ----
        var connected = await Save(center.Id, owner.Id, Password, "Shadow", secret, token1, 0);
        Assert.True(connected.IsSuccess, connected.IsSuccess ? null : connected.Error.Message);
        Assert.True(connected.Value.Configured);
        Assert.Equal("Shadow", connected.Value.Mode);
        Assert.Equal(account, connected.Value.AccountNumber);
        Assert.True(connected.Value.AccountMatchesSettings);
        Assert.StartsWith("/api/v1/payments/webhooks/sepay/", connected.Value.WebhookPath, StringComparison.Ordinal);
        Assert.True(connected.Value.HasWebhookSecret && connected.Value.HasApiToken);

        await using (var scope = provider.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var stored = await finance.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.CenterId == center.Id);
            Assert.Equal(token1, protector.Unprotect(stored.ApiToken, stored.KeyId, SecretPurposes.ApiToken(center.Id, "Sepay")));
            Assert.Equal(secret, protector.Unprotect(stored.WebhookSecret, stored.KeyId, SecretPurposes.WebhookSecret(center.Id, "Sepay")));
            Assert.NotNull(stored.NextPullAtUtc);
            var audit = await finance.PaymentAuditEvents.AsNoTracking().Where(x => x.CenterId == center.Id).ToListAsync();
            Assert.Equal("provider_connected", Assert.Single(audit).Action);
            Assert.DoesNotContain(token1, audit[0].AfterJson!, StringComparison.Ordinal);
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var mail = await identity.EmailOutboxMessages.AsNoTracking().Where(x => x.Recipient == owner.Email).ToListAsync();
            Assert.Contains("Kết nối SePay lần đầu", Assert.Single(mail).Body, StringComparison.Ordinal);
            Assert.DoesNotContain(token1, mail[0].Body, StringComparison.Ordinal);
            Assert.Contains(SecurityAuditEventTypes.Reauthenticated, await identity.SecurityAuditEvents.AsNoTracking()
                .Where(x => x.UserId == owner.Id).Select(x => x.EventType).ToListAsync());
            Assert.Contains(SecurityAuditEventTypes.ReauthenticationFailed, await identity.SecurityAuditEvents.AsNoTracking()
                .Where(x => x.UserId == owner.Id).Select(x => x.EventType).ToListAsync());
        }

        // ---- stale version; then the token alone changes and the secret is kept ----
        Assert.Equal(PaymentErrors.ConfigVersion, (await Save(center.Id, owner.Id, Password, "Shadow", null, token2, 0)).Error);
        var changed = await Save(center.Id, owner.Id, Password, "Shadow", null, token2, connected.Value.Version);
        Assert.True(changed.IsSuccess);
        Assert.True(changed.Value.Version > connected.Value.Version);

        // ---- saving again without changes writes nothing new ----
        var same = await Save(center.Id, owner.Id, Password, "Shadow", null, null, changed.Value.Version);
        Assert.Equal(changed.Value.Version, same.Value.Version);

        // ---- the same token cannot serve a second center ----
        await using (var scope = provider.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var settings = FinanceSettings.Default(otherCenter.Id);
            settings.Update("970436", account + "1", "TRUNG TAM PAY HAI", 7, true, googleOwner.Id, past);
            finance.FinanceSettings.Add(settings);
            await finance.SaveChangesAsync();
        }
        Assert.Equal(PaymentErrors.TokenInUse, (await Save(otherCenter.Id, owner.Id, Password, "Shadow", secret, token2, 0)).Error);

        await using (var scope = provider.CreateAsyncScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var stored = await finance.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.CenterId == center.Id);
            Assert.Equal(token2, protector.Unprotect(stored.ApiToken, stored.KeyId, SecretPurposes.ApiToken(center.Id, "Sepay")));
            Assert.Equal(secret, protector.Unprotect(stored.WebhookSecret, stored.KeyId, SecretPurposes.WebhookSecret(center.Id, "Sepay")));
            Assert.Equal(2, await finance.PaymentAuditEvents.CountAsync(x => x.CenterId == center.Id));
            Assert.False(await finance.PaymentProviderConfigs.AnyAsync(x => x.CenterId == otherCenter.Id));
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var mail = await identity.EmailOutboxMessages.AsNoTracking().Where(x => x.Recipient == owner.Email).ToListAsync();
            Assert.Equal(2, mail.Count);
            Assert.Contains(mail, x => x.Body.Contains("Đổi API token", StringComparison.Ordinal));
            Assert.False(await identity.EmailOutboxMessages.AnyAsync(x => x.Recipient == googleOwner.Email));
        }
    }

    private static Center NewCenter(string name, DateTimeOffset past, Guid ownerId)
    {
        var center = Center.Create($"{name} LLC", name,
            RandomNumberGenerator.GetInt32(1_000_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture),
            null, "Test only", $"{Guid.NewGuid():N}@example.invalid", "0900000000", past);
        center.Approve(ownerId, past);
        return center;
    }

    private static ServiceProvider BuildServices(string connectionString, DateTimeOffset now)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:EncryptionKeys:t1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Payments:ActiveKeyId"] = "t1",
            ["Payments:FingerprintKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSharedInfrastructure();
        services.AddIdentityAccessInfrastructure(connectionString);
        services.AddOrganizationsInfrastructure(connectionString);
        services.AddFinanceInfrastructure(connectionString);
        services.RemoveAll<IClock>();
        services.AddSingleton<IClock>(new FixedClock(now));
        return services.BuildServiceProvider();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
