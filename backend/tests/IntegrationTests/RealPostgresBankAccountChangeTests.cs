using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.Finance.Application.Payments;
using TutorManagement.Modules.Finance.Application.Tuition;
using TutorManagement.Modules.Finance.Domain.Payments;
using TutorManagement.Modules.Finance.Infrastructure.Persistence;
using TutorManagement.Modules.Finance.Infrastructure.Persistence.Repositories;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

/// <summary>D7 against real PostgreSQL: only the owner, with the password, moves the account tuition is paid into.</summary>
public sealed class RealPostgresBankAccountChangeTests
{
    private static readonly RequestOrigin Origin = new(null, null, null);

    [LocalPostgresFact]
    public async Task Only_the_owner_with_the_password_changes_the_receiving_account_and_every_owner_is_told()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var now = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero);
        var clock = new Clock(now);
        var marker = Guid.NewGuid().ToString("N")[..10];
        var owner = User.CreateExternal($"owner-{marker}@example.invalid", now.AddDays(-9));
        var manager = Guid.NewGuid();
        var center = Guid.NewGuid();
        await using var data = new PaymentTestData(connectionString);
        data.Center(center);
        data.Email(owner.Email);
        var account = RandomNumberGenerator.GetInt32(100_000_000, int.MaxValue).ToString(CultureInfo.InvariantCulture);
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var financeOptions = new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connectionString).Options;
        var services = new ServiceCollection().BuildServiceProvider();
        var password = new StubReauthenticator();
        await using (var identity = new IdentityDbContext(identityOptions))
        {
            identity.Users.Add(owner);
            await identity.SaveChangesAsync();
        }

        async Task<TutorManagement.SharedKernel.Primitives.Result<FinanceSettingsDto>> Save(Guid actor, bool isOwner, string? bin,
            string? number, string? name, int dueDays, int version, string? currentPassword = "pw")
        {
            await using var db = new FinanceDbContext(financeOptions);
            return await new FinanceSettingsWorkflow(new TuitionStore(db, clock, services), password, new FixedOwners(owner.Id), clock)
                .UpdateAsync(center, actor, isOwner, bin, number, name, dueDays, true, version, currentPassword, Origin, default);
        }
        async Task<(int Audits, int Emails)> Counts()
        {
            await using var db = new FinanceDbContext(financeOptions);
            await using var identity = new IdentityDbContext(identityOptions);
            return (await db.PaymentAuditEvents.CountAsync(x => x.CenterId == center),
                await identity.EmailOutboxMessages.CountAsync(x => x.Recipient == owner.Email));
        }

        // ---- a manager may change due days without a password ----
        var dueOnly = await Save(manager, false, null, null, null, 10, 0, currentPassword: null);
        Assert.True(dueOnly.IsSuccess);
        Assert.Equal(0, password.Calls);

        // ---- the first bank account is already a bank change: owner only, password asked ----
        Assert.Equal(TuitionErrors.BankChangeOwnerOnly, (await Save(manager, false, "970436", account, "Trung Tam", 10, 1)).Error);
        password.Result = ReauthenticationResult.Invalid;
        Assert.Equal(PaymentErrors.InvalidPassword, (await Save(owner.Id, true, "970436", account, "Trung Tam", 10, 1)).Error);
        password.Result = ReauthenticationResult.NoLocalPassword;
        Assert.Equal(PaymentErrors.PasswordRequired, (await Save(owner.Id, true, "970436", account, "Trung Tam", 10, 1)).Error);
        Assert.Equal((0, 0), await Counts());

        password.Result = ReauthenticationResult.Verified;
        var first = await Save(owner.Id, true, "970436", account, "Trung Tam", 10, 1);
        Assert.True(first.IsSuccess);
        Assert.Equal("TRUNG TAM", first.Value.BankAccountName);
        Assert.Equal((1, 1), await Counts());

        // ---- same account typed differently is not a change; a manager can still edit the rest ----
        var calls = password.Calls;
        var sameAccount = await Save(manager, false, "970436", $" {account} ", "trung tam", 5, first.Value.Version, null);
        Assert.True(sameAccount.IsSuccess);
        Assert.Equal(calls, password.Calls);
        Assert.Equal((1, 1), await Counts());

        // ---- a connected bank feed follows the new account; removing the account turns the feed off ----
        var protector = PaymentIngestionDomainTests.Protector("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ("k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        await using (var db = new FinanceDbContext(financeOptions))
        {
            var feed = PaymentProviderConfig.Create(center, PaymentProvider.Sepay, "970436", account,
                PaymentIngestionDomainTests.Secrets(protector, center, $"tok-{marker}"), owner.Id, now);
            feed.ChangeMode(PaymentProviderMode.Shadow, owner.Id, now);
            db.PaymentProviderConfigs.Add(feed);
            await db.SaveChangesAsync();
        }
        var moved = await Save(owner.Id, true, "970422", account + "9", "Trung Tam Moi", 5, sameAccount.Value.Version);
        Assert.True(moved.IsSuccess);
        await using (var db = new FinanceDbContext(financeOptions))
        {
            var feed = await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.CenterId == center);
            Assert.Equal(("970422", account + "9", PaymentProviderMode.Shadow), (feed.BankBin, feed.AccountNumber, feed.Mode));
            var audit = await db.PaymentAuditEvents.AsNoTracking().Where(x => x.CenterId == center)
                .OrderBy(x => x.AtUtc).ThenBy(x => x.Id).ToListAsync();
            Assert.All(audit, x => Assert.Equal("bank_account_changed", x.Action));
            Assert.Contains(audit, x => x.AfterJson!.Contains(account + "9", StringComparison.Ordinal));
        }
        await using (var identity = new IdentityDbContext(identityOptions))
        {
            var mail = await identity.EmailOutboxMessages.AsNoTracking().Where(x => x.Recipient == owner.Email).ToListAsync();
            Assert.Equal(2, mail.Count);
            Assert.All(mail, x => Assert.Equal("Tài khoản nhận học phí vừa thay đổi", x.Subject));
            Assert.Contains(mail, x => x.Body.Contains($"*{(account + "9")[^4..]}", StringComparison.Ordinal));
        }

        var removed = await Save(owner.Id, true, null, null, null, 5, moved.Value.Version);
        Assert.True(removed.IsSuccess);
        await using (var db = new FinanceDbContext(financeOptions))
            Assert.Equal(PaymentProviderMode.Off,
                (await db.PaymentProviderConfigs.AsNoTracking().SingleAsync(x => x.CenterId == center)).Mode);

        // ---- a stale version is refused before anything changes ----
        Assert.Equal(TuitionErrors.SettingsVersion, (await Save(owner.Id, true, "970436", account, "Trung Tam", 5, 1)).Error);
    }

    private sealed class Clock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
