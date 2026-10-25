using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class PasswordRecoveryFlowTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Otp_is_eight_digits_and_is_bound_to_user_and_token()
    {
        var service = CreateOtpService();
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var issued = service.Create(userId, tokenId);

        Assert.Matches("^[0-9]{8}$", issued.Value);
        Assert.DoesNotContain(issued.Value, issued.Hash, StringComparison.Ordinal);
        Assert.True(service.Verify(userId, tokenId, issued.Value, issued.Hash));
        Assert.False(service.Verify(Guid.NewGuid(), tokenId, issued.Value, issued.Hash));
        Assert.False(service.Verify(userId, Guid.NewGuid(), issued.Value, issued.Hash));
    }

    [Fact]
    public void Password_policy_rejects_common_patterns_and_accepts_strong_password()
    {
        Assert.False(PasswordPolicy.IsValid("Password123!"));
        Assert.False(PasswordPolicy.IsValid("shortA1!"));
        Assert.True(PasswordPolicy.IsValid("NewStrongPass!456"));
    }

    [Fact]
    public async Task Forgot_is_enumeration_safe_and_does_not_queue_email_for_unknown_account()
    {
        var fixture = new Fixture();
        var result = await fixture.Forgot.HandleAsync(
            new ForgotPasswordCommand("unknown@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(fixture.Outbox.Messages);
        Assert.Empty(fixture.Tokens.Items);
    }

    [Fact]
    public async Task Forgot_supersedes_old_code_and_caps_issues_to_five_in_rolling_day()
    {
        var fixture = new Fixture();
        for (var index = 0; index < PasswordResetPolicy.MaximumSendsPerWindow; index++)
        {
            var result = await fixture.Forgot.HandleAsync(
                new ForgotPasswordCommand(fixture.User.Email), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.Equal(index + 1, fixture.Tokens.Items.Count);
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(30);
            await fixture.Forgot.HandleAsync(new ForgotPasswordCommand(fixture.User.Email), CancellationToken.None);
            Assert.Equal(index + 1, fixture.Tokens.Items.Count);
            fixture.Clock.Now = fixture.Clock.Now.AddSeconds(31);
        }

        await fixture.Forgot.HandleAsync(new ForgotPasswordCommand(fixture.User.Email), CancellationToken.None);
        Assert.Equal(5, fixture.Tokens.Items.Count);
        Assert.Equal(5, fixture.Outbox.Messages.Count);
        Assert.All(fixture.Tokens.Items.Take(4), token => Assert.False(token.IsActive(fixture.Clock.Now)));
        Assert.True(fixture.Tokens.Items.Last().IsActive(fixture.Clock.Now));
        Assert.Equal(PasswordResetPolicy.OtpLifetime,
            fixture.Tokens.Items.Last().ExpiresAtUtc - fixture.Tokens.Items.Last().CreatedAtUtc);
    }

    [Fact]
    public async Task Five_wrong_attempts_revoke_otp_even_if_correct_code_is_later_provided()
    {
        var fixture = new Fixture();
        var otp = await fixture.IssueOtpAsync();
        var wrong = otp[0] == '0' ? "1" + otp[1..] : "0" + otp[1..];
        for (var index = 0; index < PasswordResetPolicy.MaximumFailedAttempts; index++)
        {
            var result = await fixture.Reset.HandleAsync(
                new ResetPasswordCommand(fixture.User.Email, wrong, "NewStrongPass!456"), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("invalid_password_reset_otp", result.Error.Code);
        }

        Assert.Equal(5, fixture.Tokens.Items.Single().FailedAttemptCount);
        Assert.False(fixture.Tokens.Items.Single().IsActive(fixture.Clock.Now));
        var afterLockout = await fixture.Reset.HandleAsync(
            new ResetPasswordCommand(fixture.User.Email, otp, "NewStrongPass!456"), CancellationToken.None);
        Assert.True(afterLockout.IsFailure);
        Assert.True(fixture.Passwords.Verify(fixture.User.PasswordHash!, "OldStrongPass!123").Succeeded);
    }

    [Fact]
    public async Task Successful_reset_consumes_otp_changes_hash_and_revokes_every_session()
    {
        var fixture = new Fixture();
        var otp = await fixture.IssueOtpAsync();
        var loginSecurity = await fixture.LoginSecurity.AcquireAsync(fixture.User.Id, CancellationToken.None);
        for (var attempt = 0; attempt < 5; attempt++) loginSecurity.RecordFailure(fixture.Clock.Now);
        Assert.True(loginSecurity.IsBlocked(fixture.Clock.Now));
        var refresh1 = RefreshToken.Create(fixture.User.Id, "hash-1", fixture.Clock.Now.AddDays(30), fixture.Clock.Now);
        var refresh2 = RefreshToken.Create(fixture.User.Id, "hash-2", fixture.Clock.Now.AddDays(30), fixture.Clock.Now);
        fixture.Refresh.Items.AddRange([refresh1, refresh2]);
        fixture.Sessions.Items.AddRange([
            AuthSession.Create(refresh1.FamilyId, fixture.User.Id, fixture.Clock.Now),
            AuthSession.Create(refresh2.FamilyId, fixture.User.Id, fixture.Clock.Now)
        ]);

        var result = await fixture.Reset.HandleAsync(
            new ResetPasswordCommand(fixture.User.Email, otp, "NewStrongPass!456"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fixture.Passwords.Verify(fixture.User.PasswordHash!, "NewStrongPass!456").Succeeded);
        Assert.NotNull(fixture.Tokens.Items.Single().UsedAtUtc);
        Assert.All(fixture.Sessions.Items, session => Assert.False(session.IsActive));
        Assert.All(fixture.Refresh.Items, token => Assert.False(token.IsActive(fixture.Clock.Now)));
        Assert.Equal(2, fixture.Outbox.Messages.Count);
        Assert.Equal(0, loginSecurity.FailedAttemptCount);
        Assert.Contains(fixture.Audit.Events, audit => audit.EventType == SecurityAuditEventTypes.PasswordReset);
        Assert.True((await fixture.Reset.HandleAsync(
            new ResetPasswordCommand(fixture.User.Email, otp, "AnotherStrong!456"), CancellationToken.None)).IsFailure);
    }

    [Fact]
    public async Task Change_password_keeps_current_session_and_revokes_other_devices()
    {
        var fixture = new Fixture();
        var loginSecurity = await fixture.LoginSecurity.AcquireAsync(fixture.User.Id, CancellationToken.None);
        loginSecurity.RecordFailure(fixture.Clock.Now);
        var current = RefreshToken.Create(fixture.User.Id, "current-hash", fixture.Clock.Now.AddDays(30), fixture.Clock.Now);
        var other = RefreshToken.Create(fixture.User.Id, "other-hash", fixture.Clock.Now.AddDays(30), fixture.Clock.Now);
        fixture.Refresh.Items.AddRange([current, other]);
        fixture.Sessions.Items.AddRange([
            AuthSession.Create(current.FamilyId, fixture.User.Id, fixture.Clock.Now),
            AuthSession.Create(other.FamilyId, fixture.User.Id, fixture.Clock.Now)
        ]);

        var result = await fixture.Change.HandleAsync(
            new ChangePasswordCommand(fixture.User.Id, current.FamilyId, "current-hash",
                "OldStrongPass!123", "NewStrongPass!456"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-access", result.Value.AccessToken);
        Assert.Equal("new-raw-refresh", result.Value.RefreshToken);
        Assert.True(fixture.Sessions.Items.Single(session => session.Id == current.FamilyId).IsActive);
        Assert.False(fixture.Sessions.Items.Single(session => session.Id == other.FamilyId).IsActive);
        Assert.False(current.IsActive(fixture.Clock.Now));
        Assert.False(other.IsActive(fixture.Clock.Now));
        Assert.True(fixture.Refresh.Items.Single(token => token.TokenHash == "new-hash").IsActive(fixture.Clock.Now));
        Assert.True(fixture.Passwords.Verify(fixture.User.PasswordHash!, "NewStrongPass!456").Succeeded);
        Assert.Single(fixture.Outbox.Messages);
        Assert.Equal(0, loginSecurity.FailedAttemptCount);
        Assert.Contains(fixture.Audit.Events, audit => audit.EventType == SecurityAuditEventTypes.PasswordChanged);
    }

    [Fact]
    public async Task Change_password_with_wrong_current_password_does_not_mutate_sessions()
    {
        var fixture = new Fixture();
        var current = RefreshToken.Create(fixture.User.Id, "current-hash", fixture.Clock.Now.AddDays(30), fixture.Clock.Now);
        fixture.Refresh.Items.Add(current);
        fixture.Sessions.Items.Add(AuthSession.Create(current.FamilyId, fixture.User.Id, fixture.Clock.Now));

        var result = await fixture.Change.HandleAsync(
            new ChangePasswordCommand(fixture.User.Id, current.FamilyId, "current-hash",
                "incorrect", "NewStrongPass!456"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("invalid_current_password", result.Error.Code);
        Assert.True(current.IsActive(fixture.Clock.Now));
        Assert.True(fixture.Sessions.Items.Single().IsActive);
        Assert.Empty(fixture.Outbox.Messages);
    }

    private static PasswordResetOtpService CreateOtpService() =>
        new(Options.Create(new JwtOptions { SigningKey = Convert.ToBase64String(new byte[64]) }));

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new() { Now = Start };
        public PasswordService Passwords { get; } = new();
        public User User { get; }
        public FakeTokens Tokens { get; } = new();
        public FakeSessions Sessions { get; } = new();
        public FakeRefresh Refresh { get; } = new();
        public FakeOutbox Outbox { get; } = new();
        public FakeLoginSecurity LoginSecurity { get; } = new();
        public FakeAudit Audit { get; } = new();
        public ForgotPasswordCommandHandler Forgot { get; }
        public ResetPasswordCommandHandler Reset { get; }
        public ChangePasswordCommandHandler Change { get; }

        public Fixture()
        {
            User = User.Create("owner", "owner@example.com", "+84912345678",
                Passwords.Hash("OldStrongPass!123"), Clock.Now.AddDays(-1));
            User.VerifyEmail(Clock.Now.AddHours(-1));
            var users = new FakeUsers(User);
            var otpService = CreateOtpService();
            var unitOfWork = new FakeUnitOfWork();
            Forgot = new ForgotPasswordCommandHandler(users, Tokens, otpService, Outbox, unitOfWork, Clock);
            Reset = new ResetPasswordCommandHandler(users, Tokens, otpService, Passwords, Sessions, Refresh,
                Outbox, LoginSecurity, Audit, unitOfWork, Clock);
            Change = new ChangePasswordCommandHandler(users, Refresh, Sessions, Tokens, Passwords,
                new FakeTokenService(Clock), Outbox, LoginSecurity, Audit, unitOfWork, Clock);
        }

        public async Task<string> IssueOtpAsync()
        {
            var result = await Forgot.HandleAsync(new ForgotPasswordCommand(User.Email), CancellationToken.None);
            Assert.True(result.IsSuccess);
            var match = Regex.Match(Outbox.Messages.Last().Body, @"\b\d{8}\b");
            Assert.True(match.Success);
            return match.Value;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; }
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeUsers(User user) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid userId, CancellationToken token) =>
            Task.FromResult<User?>(user.Id == userId ? user : null);
        public Task<User?> FindByNormalizedEmailAsync(string email, CancellationToken token) =>
            Task.FromResult<User?>(email == user.NormalizedEmail ? user : null);
        public Task<User?> FindByNormalizedUsernameAsync(string username, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedUsernameAsync(string username, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedEmailAsync(string email, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ExistsByPhoneNumberAsync(string phone, CancellationToken token) => throw new NotSupportedException();
        public Task AddAsync(User newUser, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class FakeTokens : IUserSecurityTokenRepository
    {
        public List<UserSecurityToken> Items { get; } = [];
        public Task AddAsync(UserSecurityToken token, CancellationToken cancellationToken)
        {
            Items.Add(token);
            return Task.CompletedTask;
        }
        public Task<UserSecurityToken?> FindByHashAndPurposeAsync(string hash, string purpose, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(token => token.TokenHash == hash && token.Purpose == purpose));
        public Task<IReadOnlyCollection<UserSecurityToken>> ListUnconsumedByUserAndPurposeAsync(Guid userId, string purpose, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<UserSecurityToken>>(Items.Where(token => token.UserId == userId &&
                token.Purpose == purpose && token.UsedAtUtc is null && token.RevokedAtUtc is null).ToArray());
        public Task<bool> WasCreatedSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(token => token.UserId == userId && token.Purpose == purpose && token.CreatedAtUtc >= since));
        public Task<int> CountCreatedSinceAsync(Guid userId, string purpose, DateTimeOffset since, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Count(token => token.UserId == userId && token.Purpose == purpose && token.CreatedAtUtc >= since));
    }

    private sealed class FakeSessions : IAuthSessionRepository
    {
        public List<AuthSession> Items { get; } = [];
        public Task AddAsync(AuthSession session, CancellationToken cancellationToken)
        {
            Items.Add(session);
            return Task.CompletedTask;
        }
        public Task<AuthSession?> FindAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(session => session.Id == sessionId));
        public Task<IReadOnlyList<AuthSession>> ListActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AuthSession>>(Items.Where(session => session.UserId == userId && session.IsActive).ToArray());
        public Task<bool> IsActiveAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(session => session.Id == sessionId && session.UserId == userId && session.IsActive));
        public Task RevokeActiveAsync(Guid sessionId, DateTimeOffset atUtc, string reason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRefresh : IRefreshTokenRepository
    {
        public List<RefreshToken> Items { get; } = [];
        public Task<RefreshToken?> FindByHashAsync(string hash, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(token => token.TokenHash == hash));
        public Task<RefreshToken?> FindByHashWithUserAsync(string hash, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            Items.Add(token);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<RefreshToken>> ListActiveByUserIdAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(token => token.UserId == userId && token.IsActive(now)).ToArray());
        public Task<IReadOnlyList<RefreshToken>> ListActiveByFamilyIdAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(token => token.FamilyId == familyId && token.IsActive(now)).ToArray());
        public Task<int> RevokeActiveFamilyAsync(Guid familyId, DateTimeOffset atUtc, string reason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeTokenService(FakeClock clock) : ITokenService
    {
        public IssuedAccessToken CreateAccessToken(User user, IReadOnlyCollection<string> roles, Guid sessionId) =>
            new("new-access", clock.Now.AddMinutes(15));
        public IssuedRefreshToken CreateRefreshToken() =>
            new("new-raw-refresh", "new-hash", clock.Now.AddDays(30));
        public string HashRefreshToken(string refreshToken) => refreshToken;
    }

    private sealed class FakeOutbox : IEmailOutboxRepository
    {
        public List<EmailOutboxMessage> Messages { get; } = [];
        public Task AddAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeLoginSecurity : ILoginSecurityRepository
    {
        private readonly Dictionary<Guid, LoginSecurityState> _states = [];
        public Task<LoginSecurityState> AcquireAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!_states.TryGetValue(userId, out var state))
                _states[userId] = state = LoginSecurityState.Create(userId);
            return Task.FromResult(state);
        }
    }

    private sealed class FakeAudit : ISecurityAuditRepository
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public Task AddAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
        public Task RecordIsolatedAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken) =>
            AddAsync(auditEvent, cancellationToken);
    }

    private sealed class FakeUnitOfWork : IIdentityUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IIdentityTransaction>(new FakeTransaction());
    }

    private sealed class FakeTransaction : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
