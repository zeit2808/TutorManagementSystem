using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class LoginProtectionTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Domain_uses_progressive_backoff_and_bounded_temporary_lock()
    {
        var state = LoginSecurityState.Create(Guid.NewGuid());
        var now = Start;
        var lastAttemptAt = Start;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            Assert.False(state.IsBlocked(now));
            lastAttemptAt = now;
            state.RecordFailure(now);
            Assert.Equal(attempt, state.FailedAttemptCount);
            if (attempt < 5) Assert.False(state.IsBlocked(now));
            now = state.BlockedUntilUtc ?? now.AddSeconds(1);
        }

        Assert.Equal(lastAttemptAt + LoginProtectionPolicy.MaximumLock, state.BlockedUntilUtc);
        Assert.Equal(10, state.FailedAttemptCount);
        Assert.True(lastAttemptAt - Start < LoginProtectionPolicy.AttemptWindow);
        state.Reset();
        Assert.Equal(0, state.FailedAttemptCount);
        Assert.False(state.IsBlocked(Start));
    }

    [Fact]
    public async Task Known_account_is_throttled_without_issuing_session_and_success_resets_state()
    {
        var fixture = new Fixture();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await fixture.Login("wrong-password");
            Assert.Equal("invalid_credentials", failed.Error.Code);
        }

        var blocked = await fixture.Login("CorrectPassword!123");
        Assert.Equal("invalid_credentials", blocked.Error.Code);
        Assert.Empty(fixture.Sessions.Items);
        Assert.Empty(fixture.Refresh.Items);
        Assert.Equal(5, fixture.Audit.Events.Count(audit => audit.EventType == "login_failed"));
        Assert.Contains(fixture.Audit.Events, audit => audit.EventType == "login_temporarily_blocked");

        fixture.Clock.Now = fixture.Clock.Now.AddSeconds(31);
        var success = await fixture.Login("CorrectPassword!123");
        Assert.True(success.IsSuccess);
        Assert.Equal(0, fixture.Security.State.FailedAttemptCount);
        Assert.Single(fixture.Sessions.Items);
        Assert.Single(fixture.Refresh.Items);
        Assert.Contains(fixture.Audit.Events, audit => audit.EventType == "login_succeeded" &&
            audit.ClientIp == "127.0.0.1" && audit.TraceId == "test-trace");
    }

    [Fact]
    public async Task Unknown_username_gets_same_error_without_creating_account_audit_rows()
    {
        var fixture = new Fixture();
        var result = await fixture.Handler.HandleAsync(
            new LoginCommand("unknown", "wrong-password"), CancellationToken.None);
        Assert.Equal("invalid_credentials", result.Error.Code);
        Assert.Empty(fixture.Audit.Events);
        Assert.False(fixture.Security.Acquired);
    }

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new() { Now = Start };
        public User User { get; }
        public FakeSecurity Security { get; }
        public FakeAudit Audit { get; } = new();
        public FakeSessions Sessions { get; } = new();
        public FakeRefresh Refresh { get; } = new();
        public LoginCommandHandler Handler { get; }

        public Fixture()
        {
            User = User.Create("owner", "owner@example.com", "+84912345678", "CorrectPassword!123", Start);
            User.VerifyEmail(Start);
            Security = new FakeSecurity(User.Id);
            Handler = new LoginCommandHandler(new FakeUsers(User), Security, Audit, Sessions, Refresh,
                new FakePasswords(), new FakeTokens(Clock), new FakeUnitOfWork(), Clock);
        }

        public Task<TutorManagement.SharedKernel.Primitives.Result<TutorManagement.Modules.IdentityAccess.Application.Authentication.AuthenticationResult>>
            Login(string password) => Handler.HandleAsync(
                new LoginCommand("owner", password, "127.0.0.1", "unit-test", "test-trace"), CancellationToken.None);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; }
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeUsers(User user) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<User?>(id == user.Id ? user : null);
        public Task<User?> FindByNormalizedUsernameAsync(string name, CancellationToken ct) =>
            Task.FromResult<User?>(name == user.NormalizedUsername ? user : null);
        public Task<User?> FindByNormalizedEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedUsernameAsync(string username, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedEmailAsync(string email, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneNumberAsync(string phone, CancellationToken ct) => throw new NotSupportedException();
        public Task AddAsync(User value, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeSecurity(Guid userId) : ILoginSecurityRepository
    {
        public LoginSecurityState State { get; } = LoginSecurityState.Create(userId);
        public bool Acquired { get; private set; }
        public Task<LoginSecurityState> AcquireAsync(Guid id, CancellationToken ct)
        {
            Acquired = true;
            return Task.FromResult(State);
        }
    }

    private sealed class FakeAudit : ISecurityAuditRepository
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Task AddAsync(SecurityAuditEvent value, CancellationToken ct)
        {
            Events.Add(value);
            return Task.CompletedTask;
        }
        public Task RecordIsolatedAsync(SecurityAuditEvent value, CancellationToken ct) => AddAsync(value, ct);
    }

    private sealed class FakePasswords : IPasswordService
    {
        public string Hash(string password) => password;
        public PasswordVerification Verify(string hash, string password) => new(hash == password, false);
        public void PerformDummyVerification(string password) { }
    }

    private sealed class FakeTokens(FakeClock clock) : ITokenService
    {
        public IssuedAccessToken CreateAccessToken(User user, IReadOnlyCollection<string> roles, Guid sessionId) =>
            new("access-value", clock.Now.AddMinutes(15));
        public IssuedRefreshToken CreateRefreshToken() => new("refresh-value", "refresh-hash", clock.Now.AddDays(30));
        public string HashRefreshToken(string refreshToken) => refreshToken;
    }

    private sealed class FakeSessions : IAuthSessionRepository
    {
        public List<AuthSession> Items { get; } = [];
        public Task AddAsync(AuthSession session, CancellationToken ct) { Items.Add(session); return Task.CompletedTask; }
        public Task<AuthSession?> FindAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuthSession>> ListActiveByUserIdAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsActiveAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeActiveAsync(Guid id, DateTimeOffset at, string reason, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeRefresh : IRefreshTokenRepository
    {
        public List<RefreshToken> Items { get; } = [];
        public Task AddAsync(RefreshToken token, CancellationToken ct) { Items.Add(token); return Task.CompletedTask; }
        public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<RefreshToken?> FindByHashWithUserAsync(string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid id, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<RefreshToken>> ListActiveByFamilyIdAsync(Guid id, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> RevokeActiveFamilyAsync(Guid id, DateTimeOffset at, string reason, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IIdentityUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Task.FromResult<IIdentityTransaction>(new FakeTransaction());
    }

    private sealed class FakeTransaction : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
