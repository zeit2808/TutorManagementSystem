using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Profile;
using TutorManagement.Modules.IdentityAccess.Contracts.Authentication;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;

namespace TutorManagement.IntegrationTests;

public sealed class ProfileSelfServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private const string Password = "OldStrongPass!123";

    [Fact]
    public void Email_change_code_is_bound_to_the_new_address()
    {
        var service = CreateOtpService();
        var userId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var issued = service.Create(userId, tokenId, "NEW@EXAMPLE.COM");

        Assert.Matches("^[0-9]{8}$", issued.Value);
        Assert.True(service.Verify(userId, tokenId, "NEW@EXAMPLE.COM", issued.Value, issued.Hash));
        Assert.False(service.Verify(userId, tokenId, "OTHER@EXAMPLE.COM", issued.Value, issued.Hash));
        Assert.False(service.Verify(Guid.NewGuid(), tokenId, "NEW@EXAMPLE.COM", issued.Value, issued.Hash));
    }

    [Fact]
    public async Task Profile_update_normalizes_phone_and_collapses_display_name()
    {
        var fixture = new Fixture();
        var result = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id,
            "  Nguyễn   Văn  An ", "0987 654 321", null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Nguyễn Văn An", fixture.User.DisplayName);
        Assert.Equal("+84987654321", fixture.User.PhoneNumber);
        Assert.Contains(fixture.Audit.Events, e => e.EventType == SecurityAuditEventTypes.ProfileUpdated);
    }

    [Fact]
    public async Task Profile_update_rejects_bad_taken_or_missing_staff_phone()
    {
        var fixture = new Fixture();
        fixture.Users.TakenPhones.Add("+84911111111");

        var invalid = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id, null,
            "12345", null, null, null), CancellationToken.None);
        var taken = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id, null,
            "0911111111", null, null, null), CancellationToken.None);
        var missing = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id, "An",
            "", null, null, null), CancellationToken.None);
        var tooLong = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id,
            new string('a', 101), "0912345678", null, null, null), CancellationToken.None);

        Assert.Equal(IdentityErrors.InvalidPhoneNumber, invalid.Error);
        Assert.Equal(IdentityErrors.PhoneNumberAlreadyExists, taken.Error);
        Assert.Equal(IdentityErrors.PhoneNumberRequired, missing.Error);
        Assert.Equal(IdentityErrors.InvalidDisplayName, tooLong.Error);
        Assert.Equal("+84912345678", fixture.User.PhoneNumber);
    }

    [Fact]
    public async Task Student_account_may_clear_its_phone()
    {
        var fixture = new Fixture(student: true);
        var result = await fixture.Update.HandleAsync(new UpdateMyProfileCommand(fixture.User.Id, "Bình",
            null, null, null, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(fixture.User.PhoneNumber);
        Assert.Equal("Bình", fixture.User.DisplayName);
    }

    [Fact]
    public async Task Email_change_sends_code_to_new_address_then_moves_email_and_tells_old_address()
    {
        var fixture = new Fixture();
        var started = await fixture.RequestChange("New.Owner@Example.com");
        Assert.True(started.IsSuccess);
        Assert.Equal("owner@example.com", fixture.User.Email);
        var codeMail = fixture.Outbox.Messages.Single();
        Assert.Equal("New.Owner@Example.com", codeMail.Recipient);
        var otp = Regex.Match(codeMail.Body, @"\b\d{8}\b").Value;

        var wrongAddress = await fixture.Confirm.HandleAsync(new ConfirmEmailChangeCommand(fixture.User.Id,
            "someone@example.com", otp, null, null, null), CancellationToken.None);
        Assert.Equal(IdentityErrors.InvalidEmailChangeOtp, wrongAddress.Error);

        var confirmed = await fixture.Confirm.HandleAsync(new ConfirmEmailChangeCommand(fixture.User.Id,
            "new.owner@example.com", otp, null, null, null), CancellationToken.None);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal("new.owner@example.com", fixture.User.Email);
        Assert.Equal("NEW.OWNER@EXAMPLE.COM", fixture.User.NormalizedEmail);
        Assert.True(fixture.User.IsEmailVerified);
        var notice = fixture.Outbox.Messages.Last();
        Assert.Equal("owner@example.com", notice.Recipient);
        Assert.DoesNotContain("new.owner@example.com", notice.Body, StringComparison.Ordinal);
        Assert.Contains(fixture.Audit.Events, e => e.EventType == SecurityAuditEventTypes.EmailChanged);

        var reused = await fixture.Confirm.HandleAsync(new ConfirmEmailChangeCommand(fixture.User.Id,
            "new.owner@example.com", otp, null, null, null), CancellationToken.None);
        Assert.Equal(IdentityErrors.InvalidEmailChangeOtp, reused.Error);
    }

    [Fact]
    public async Task Email_change_requires_password_and_rejects_same_taken_or_rapid_requests()
    {
        var fixture = new Fixture();
        fixture.Users.TakenEmails.Add("TAKEN@EXAMPLE.COM");

        var wrongPassword = await fixture.Request.HandleAsync(new RequestEmailChangeCommand(fixture.User.Id,
            "new@example.com", "nope", null, null, null), CancellationToken.None);
        var same = await fixture.RequestChange("OWNER@example.com");
        var taken = await fixture.RequestChange("taken@example.com");
        var first = await fixture.RequestChange("new@example.com");
        var tooSoon = await fixture.RequestChange("new@example.com");

        Assert.Equal(IdentityErrors.InvalidCurrentPassword, wrongPassword.Error);
        Assert.Equal(IdentityErrors.EmailUnchanged, same.Error);
        Assert.Equal(IdentityErrors.EmailAlreadyExists, taken.Error);
        Assert.True(first.IsSuccess);
        Assert.Equal(IdentityErrors.EmailChangeTooFrequent, tooSoon.Error);

        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(2);
        var resent = await fixture.RequestChange("new@example.com");
        Assert.True(resent.IsSuccess);
        Assert.Single(fixture.Tokens.Items, token => token.IsActive(fixture.Clock.Now));
    }

    [Fact]
    public async Task Google_only_account_cannot_change_email()
    {
        var fixture = new Fixture(student: true);
        var result = await fixture.RequestChange("new@example.com");
        Assert.Equal(IdentityErrors.EmailChangeRequiresPassword, result.Error);
    }

    [Fact]
    public async Task Five_wrong_codes_burn_the_email_change_code()
    {
        var fixture = new Fixture();
        Assert.True((await fixture.RequestChange("new@example.com")).IsSuccess);
        var otp = Regex.Match(fixture.Outbox.Messages.Single().Body, @"\b\d{8}\b").Value;
        var wrong = otp == "00000000" ? "11111111" : "00000000";
        for (var attempt = 0; attempt < EmailChangePolicy.MaximumFailedAttempts; attempt++)
            await fixture.Confirm.HandleAsync(new ConfirmEmailChangeCommand(fixture.User.Id, "new@example.com",
                wrong, null, null, null), CancellationToken.None);

        var late = await fixture.Confirm.HandleAsync(new ConfirmEmailChangeCommand(fixture.User.Id,
            "new@example.com", otp, null, null, null), CancellationToken.None);
        Assert.Equal(IdentityErrors.InvalidEmailChangeOtp, late.Error);
        Assert.Equal("owner@example.com", fixture.User.Email);
    }

    [Fact]
    public void Masked_email_keeps_the_domain_only()
    {
        Assert.Equal("an***@gmail.com", ProfileMessageFactory.Mask("annguyen@gmail.com"));
        Assert.Equal("a***@x.vn", ProfileMessageFactory.Mask("a@x.vn"));
    }

    private static EmailChangeOtpService CreateOtpService() =>
        new(Options.Create(new JwtOptions { SigningKey = Convert.ToBase64String(new byte[64]) }));

    private sealed class Fixture
    {
        public FakeClock Clock { get; } = new() { Now = Start };
        public User User { get; }
        public FakeUsers Users { get; }
        public FakeTokens Tokens { get; } = new();
        public FakeOutbox Outbox { get; } = new();
        public FakeAudit Audit { get; } = new();
        public UpdateMyProfileCommandHandler Update { get; }
        public RequestEmailChangeCommandHandler Request { get; }
        public ConfirmEmailChangeCommandHandler Confirm { get; }

        public Fixture(bool student = false)
        {
            if (student)
            {
                User = User.CreateExternal("student@gmail.com", Clock.Now.AddDays(-1));
                User.AssignRole(SystemRoles.StudentId);
            }
            else
            {
                User = User.Create("owner", "owner@example.com", "+84912345678", "hash", Clock.Now.AddDays(-1));
            }
            User.VerifyEmail(Clock.Now.AddHours(-1));
            Users = new FakeUsers(User);
            var otp = CreateOtpService();
            var unitOfWork = new FakeUnitOfWork();
            Update = new UpdateMyProfileCommandHandler(Users, Audit, unitOfWork, Clock);
            Request = new RequestEmailChangeCommandHandler(new FakeReauthenticator(User), Users, Tokens, otp,
                Outbox, Audit, unitOfWork, Clock);
            Confirm = new ConfirmEmailChangeCommandHandler(Users, Tokens, otp, Outbox, Audit, unitOfWork, Clock);
        }

        public Task<TutorManagement.SharedKernel.Primitives.Result<EmailChangeStarted>> RequestChange(string email) =>
            Request.HandleAsync(new RequestEmailChangeCommand(User.Id, email, Password, null, null, null),
                CancellationToken.None);
    }

    private sealed class FakeReauthenticator(User user) : IPasswordReauthenticator
    {
        public Task<ReauthenticationResult> VerifyAsync(Guid userId, string password, string? clientIp,
            string? userAgent, string? traceId, CancellationToken cancellationToken) =>
            Task.FromResult(!user.HasLocalPassword ? ReauthenticationResult.NoLocalPassword
                : userId == user.Id && password == Password ? ReauthenticationResult.Verified
                : ReauthenticationResult.Invalid);
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset Now { get; set; }
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeUsers(User user) : IUserRepository
    {
        public HashSet<string> TakenEmails { get; } = [];
        public HashSet<string> TakenPhones { get; } = [];
        public Task<User?> FindByIdAsync(Guid userId, CancellationToken token) =>
            Task.FromResult<User?>(user.Id == userId ? user : null);
        public Task<User?> FindByNormalizedEmailAsync(string email, CancellationToken token) =>
            Task.FromResult<User?>(email == user.NormalizedEmail ? user : null);
        public Task<User?> FindByNormalizedUsernameAsync(string username, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedUsernameAsync(string username, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> ExistsByNormalizedEmailAsync(string email, CancellationToken token) =>
            Task.FromResult(email == user.NormalizedEmail || TakenEmails.Contains(email));
        public Task<bool> ExistsByPhoneNumberAsync(string phone, CancellationToken token) =>
            Task.FromResult(phone == user.PhoneNumber || TakenPhones.Contains(phone));
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

    private sealed class FakeOutbox : IEmailOutboxRepository
    {
        public List<EmailOutboxMessage> Messages { get; } = [];
        public Task AddAsync(EmailOutboxMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
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
