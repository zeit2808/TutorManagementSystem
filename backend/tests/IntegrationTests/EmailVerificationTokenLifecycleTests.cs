using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class EmailVerificationTokenLifecycleTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Revoked_token_is_inactive_and_cannot_be_used()
    {
        var token = CreateToken("old-hash");

        token.Revoke(NowUtc.AddMinutes(2), UserSecurityTokenRevocationReasons.Superseded);

        Assert.False(token.IsActive(NowUtc.AddMinutes(3)));
        Assert.Equal(NowUtc.AddMinutes(2), token.RevokedAtUtc);
        Assert.Equal(UserSecurityTokenRevocationReasons.Superseded, token.RevocationReason);
        Assert.Throws<InvalidOperationException>(() => token.MarkUsed(NowUtc.AddMinutes(3)));
    }

    [Fact]
    public async Task Resend_revokes_previous_token_before_issuing_a_new_token()
    {
        var user = User.Create(
            "center-owner",
            "owner@example.com",
            "+84912345678",
            "password-hash",
            NowUtc.AddDays(-1));
        var previousToken = UserSecurityToken.Create(
            user.Id,
            UserSecurityTokenPurposes.EmailVerification,
            "previous-hash",
            NowUtc.AddHours(12),
            NowUtc.AddHours(-12));
        var tokenRepository = new InMemorySecurityTokenRepository(previousToken);
        var emailRepository = new RecordingEmailOutboxRepository();
        var unitOfWork = new RecordingUnitOfWork();
        var handler = new ResendVerificationEmailCommandHandler(
            new StubUserRepository(user),
            tokenRepository,
            emailRepository,
            new StubOneTimeTokenService(),
            unitOfWork,
            new StubClock());

        var result = await handler.HandleAsync(
            new ResendVerificationEmailCommand(user.Email),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NowUtc, previousToken.RevokedAtUtc);
        Assert.Equal(
            UserSecurityTokenRevocationReasons.Superseded,
            previousToken.RevocationReason);

        var newToken = Assert.Single(tokenRepository.AddedTokens);
        Assert.True(newToken.IsActive(NowUtc));
        Assert.Equal(NowUtc.AddHours(24), newToken.ExpiresAtUtc);
        Assert.Single(emailRepository.Messages);
        Assert.Equal(2, unitOfWork.SaveCount);
        Assert.Equal(newToken.Id, Assert.Single(emailRepository.Messages).SecurityTokenId);
    }

    private static UserSecurityToken CreateToken(string hash) =>
        UserSecurityToken.Create(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            UserSecurityTokenPurposes.EmailVerification,
            hash,
            NowUtc.AddHours(24),
            NowUtc);

    private sealed class StubClock : IClock
    {
        public DateTimeOffset UtcNow => NowUtc;
    }

    private sealed class StubOneTimeTokenService : IOneTimeTokenService
    {
        public IssuedOneTimeToken Create() => new("new-raw-token", "new-token-hash");

        public string Hash(string token) => $"hash:{token}";
    }

    private sealed class StubUserRepository(User user) : IUserRepository
    {
        public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user.Id == userId ? user : null);

        public Task<User?> FindByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user);

        public Task<User?> FindByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByNormalizedUsernameAsync(
            string normalizedUsername,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByNormalizedEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByPhoneNumberAsync(
            string phoneNumber,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(User newUser, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InMemorySecurityTokenRepository(UserSecurityToken previousToken)
        : IUserSecurityTokenRepository
    {
        public List<UserSecurityToken> AddedTokens { get; } = [];

        public Task<UserSecurityToken?> FindByHashAndPurposeAsync(
            string tokenHash,
            string purpose,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task AddAsync(
            UserSecurityToken token,
            CancellationToken cancellationToken)
        {
            AddedTokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<UserSecurityToken>>
            ListUnconsumedByUserAndPurposeAsync(
                Guid userId,
                string purpose,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<UserSecurityToken>>([previousToken]);

        public Task<bool> WasCreatedSinceAsync(
            Guid userId,
            string purpose,
            DateTimeOffset createdSinceUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> CountCreatedSinceAsync(
            Guid userId, string purpose, DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class RecordingEmailOutboxRepository : IEmailOutboxRepository
    {
        public List<EmailOutboxMessage> Messages { get; } = [];

        public Task AddAsync(
            EmailOutboxMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IIdentityUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }

        public Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IIdentityTransaction>(new NoOpTransaction());
    }

    private sealed class NoOpTransaction : IIdentityTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
