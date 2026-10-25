using TutorManagement.Application.Abstractions.Clock;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.AcceptCenterInvitation;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.InviteCenterOwner;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.Organizations.Contracts.Centers;

namespace TutorManagement.IntegrationTests;

public sealed class CenterInvitationFlowTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 10, 17, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid CenterId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid AdminId = Guid.Parse("50000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task Approved_center_can_queue_owner_invitation_without_storing_raw_token()
    {
        var invitations = new InvitationRepository();
        var outbox = new OutboxRepository();
        var unitOfWork = new UnitOfWork();
        var handler = new InviteCenterOwnerCommandHandler(
            new CenterReader(isApproved: true),
            new PlatformAdminReader(isActive: true),
            new UserRepository(),
            invitations,
            outbox,
            new OneTimeTokenService(),
            unitOfWork,
            new Clock());

        var result = await handler.HandleAsync(
            new InviteCenterOwnerCommand(CenterId, "owner@example.com", AdminId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var invitation = Assert.Single(invitations.Items);
        Assert.Equal("HASHED_INVITATION_TOKEN", invitation.TokenHash);
        Assert.DoesNotContain("raw-invitation-token", invitation.TokenHash, StringComparison.Ordinal);
        Assert.Equal(NowUtc.AddHours(48), invitation.ExpiresAtUtc);
        Assert.Contains("raw-invitation-token", Assert.Single(outbox.Items).Body, StringComparison.Ordinal);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Deleted_or_revoked_admin_cannot_queue_an_invitation()
    {
        var invitations = new InvitationRepository();
        var outbox = new OutboxRepository();
        var unitOfWork = new UnitOfWork();
        var handler = new InviteCenterOwnerCommandHandler(
            new CenterReader(isApproved: true),
            new PlatformAdminReader(isActive: false),
            new UserRepository(),
            invitations,
            outbox,
            new OneTimeTokenService(),
            unitOfWork,
            new Clock());

        var result = await handler.HandleAsync(
            new InviteCenterOwnerCommand(CenterId, "owner@example.com", AdminId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.InvalidInviterSession, result.Error);
        Assert.Empty(invitations.Items);
        Assert.Empty(outbox.Items);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Admin_deleted_during_save_returns_invalid_session_instead_of_database_error()
    {
        var handler = new InviteCenterOwnerCommandHandler(
            new CenterReader(isApproved: true),
            new PlatformAdminReader(isActive: true),
            new UserRepository(),
            new InvitationRepository(),
            new OutboxRepository(),
            new OneTimeTokenService(),
            new UnitOfWork
            {
                ForeignKeyConstraint = "FK_center_invitations_users_invited_by_user_id"
            },
            new Clock());

        var result = await handler.HandleAsync(
            new InviteCenterOwnerCommand(CenterId, "owner@example.com", AdminId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.InvalidInviterSession, result.Error);
    }

    [Fact]
    public async Task Accepting_invitation_atomically_creates_verified_user_and_center_owner_membership()
    {
        var invitation = CenterInvitation.CreateOwner(
            CenterId,
            "owner@example.com",
            "OWNER@EXAMPLE.COM",
            "HASHED_INVITATION_TOKEN",
            NowUtc.AddHours(48),
            NowUtc,
            AdminId);
        var invitations = new InvitationRepository(invitation);
        var users = new UserRepository();
        var memberships = new MembershipRepository();
        var unitOfWork = new UnitOfWork();
        var handler = new AcceptCenterInvitationCommandHandler(
            invitations,
            memberships,
            users,
            new CenterReader(isApproved: true),
            new OneTimeTokenService(),
            new PasswordService(),
            unitOfWork,
            new Clock());

        var result = await handler.HandleAsync(
            new AcceptCenterInvitationCommand(
                "raw-invitation-token",
                "center.owner",
                "StrongPassword!123",
                "0912345678"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(users.AddedUsers);
        Assert.True(user.IsEmailVerified);
        Assert.Equal("+84912345678", user.PhoneNumber);
        var membership = Assert.Single(memberships.Items);
        Assert.Equal(user.Id, membership.UserId);
        Assert.Equal(CenterMemberRole.CenterOwner, membership.Role);
        Assert.Equal(user.Id, invitation.AcceptedByUserId);
        Assert.Equal(NowUtc, invitation.AcceptedAtUtc);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Expired_invitation_cannot_create_an_account()
    {
        var invitation = CenterInvitation.CreateOwner(
            CenterId,
            "owner@example.com",
            "OWNER@EXAMPLE.COM",
            "HASHED_INVITATION_TOKEN",
            NowUtc.AddMinutes(-1),
            NowUtc.AddDays(-3),
            AdminId);
        var users = new UserRepository();
        var memberships = new MembershipRepository();
        var unitOfWork = new UnitOfWork();
        var handler = new AcceptCenterInvitationCommandHandler(
            new InvitationRepository(invitation),
            memberships,
            users,
            new CenterReader(isApproved: true),
            new OneTimeTokenService(),
            new PasswordService(),
            unitOfWork,
            new Clock());

        var result = await handler.HandleAsync(
            new AcceptCenterInvitationCommand(
                "raw-invitation-token", "center.owner", "StrongPassword!123", "0912345678"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(IdentityErrors.InvalidCenterInvitation, result.Error);
        Assert.Empty(users.AddedUsers);
        Assert.Empty(memberships.Items);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => NowUtc;
    }

    private sealed class PlatformAdminReader(bool isActive) : IPlatformAdminReader
    {
        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(isActive && userId == AdminId);
    }

    private sealed class CenterReader(bool isApproved) : IOrganizationCenterReader
    {
        public Task<CenterAccessSnapshot?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult<CenterAccessSnapshot?>(
                centerId == CenterId ? new(centerId, "Trung tam Anh Sao", isApproved) : null);

        public Task<IReadOnlyCollection<CenterAccessSnapshot>> ListByIdsAsync(
            IReadOnlyCollection<Guid> centerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CenterAccessSnapshot>>(
                centerIds.Contains(CenterId)
                    ? [new CenterAccessSnapshot(CenterId, "Trung tam Anh Sao", isApproved)]
                    : []);
    }

    private sealed class OneTimeTokenService : IOneTimeTokenService
    {
        public IssuedOneTimeToken Create() =>
            new("raw-invitation-token", "HASHED_INVITATION_TOKEN");

        public string Hash(string token) => token == "raw-invitation-token"
            ? "HASHED_INVITATION_TOKEN"
            : "UNKNOWN";
    }

    private sealed class PasswordService : IPasswordService
    {
        public string Hash(string password) => $"HASH:{password}";
        public PasswordVerification Verify(string passwordHash, string providedPassword) => new(false, false);
        public void PerformDummyVerification(string providedPassword) { }
    }

    private sealed class UserRepository : IUserRepository
    {
        public List<User> AddedUsers { get; } = [];
        public Task<User?> FindByIdAsync(Guid userId, CancellationToken token) =>
            Task.FromResult(AddedUsers.SingleOrDefault(user => user.Id == userId));
        public Task<User?> FindByNormalizedUsernameAsync(string value, CancellationToken token) => Task.FromResult<User?>(null);
        public Task<User?> FindByNormalizedEmailAsync(string value, CancellationToken token) => Task.FromResult<User?>(null);
        public Task<bool> ExistsByNormalizedUsernameAsync(string value, CancellationToken token) => Task.FromResult(false);
        public Task<bool> ExistsByNormalizedEmailAsync(string value, CancellationToken token) => Task.FromResult(false);
        public Task<bool> ExistsByPhoneNumberAsync(string value, CancellationToken token) => Task.FromResult(false);
        public Task AddAsync(User user, CancellationToken token)
        {
            AddedUsers.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class InvitationRepository : ICenterInvitationRepository
    {
        public InvitationRepository(params CenterInvitation[] invitations) => Items.AddRange(invitations);
        public List<CenterInvitation> Items { get; } = [];
        public Task<CenterInvitation?> FindByTokenHashAsync(string hash, CancellationToken token) =>
            Task.FromResult(Items.SingleOrDefault(item => item.TokenHash == hash));
        public Task<bool> HasActiveInvitationAsync(
            Guid centerId, string email, DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(Items.Any(item =>
                item.CenterId == centerId && item.NormalizedEmail == email && item.IsActiveAt(now)));
        public Task AddAsync(CenterInvitation invitation, CancellationToken token)
        {
            Items.Add(invitation);
            return Task.CompletedTask;
        }
    }

    private sealed class MembershipRepository : ICenterMembershipRepository
    {
        public List<CenterMembership> Items { get; } = [];
        public Task<bool> ExistsAsync(Guid centerId, Guid userId, CancellationToken token) =>
            Task.FromResult(Items.Any(item => item.CenterId == centerId && item.UserId == userId));
        public Task AddAsync(CenterMembership membership, CancellationToken token)
        {
            Items.Add(membership);
            return Task.CompletedTask;
        }
    }

    private sealed class OutboxRepository : IEmailOutboxRepository
    {
        public List<EmailOutboxMessage> Items { get; } = [];
        public Task AddAsync(EmailOutboxMessage message, CancellationToken token)
        {
            Items.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWork : IIdentityUnitOfWork
    {
        public int SaveCount { get; private set; }
        public string? ForeignKeyConstraint { get; init; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (ForeignKeyConstraint is not null)
            {
                throw new IdentityReferenceException(
                    ForeignKeyConstraint,
                    new InvalidOperationException("Simulated foreign key race."));
            }
            return Task.FromResult(1);
        }

        public Task<IIdentityTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
