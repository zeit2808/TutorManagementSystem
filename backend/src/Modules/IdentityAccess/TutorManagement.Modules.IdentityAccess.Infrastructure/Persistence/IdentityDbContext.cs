using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Npgsql;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;

namespace TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options), IIdentityUnitOfWork
{
    public const string Schema = "iam";

    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalLogin> ExternalLogins => Set<ExternalLogin>();

    public DbSet<LoginSecurityState> LoginSecurityStates => Set<LoginSecurityState>();

    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<UserSecurityToken> UserSecurityTokens => Set<UserSecurityToken>();

    public DbSet<EmailOutboxMessage> EmailOutboxMessages => Set<EmailOutboxMessage>();

    public DbSet<CenterInvitation> CenterInvitations => Set<CenterInvitation>();

    public DbSet<CenterMembership> CenterMemberships => Set<CenterMembership>();

    async Task<IIdentityTransaction> IIdentityUnitOfWork.BeginTransactionAsync(CancellationToken cancellationToken) =>
        new EfIdentityTransaction(await Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    async Task<IIdentityTransaction> IIdentityUnitOfWork.BeginLoginTransactionAsync(CancellationToken cancellationToken) =>
        new EfIdentityTransaction(await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken));

    void IIdentityUnitOfWork.DiscardChanges() => ChangeTracker.Clear();

    async Task<int> IIdentityUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new IdentityConcurrencyException(exception);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            throw new IdentityConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.SerializationFailure
            })
        {
            throw new IdentityConcurrencyException(exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            } postgresException)
        {
            throw new IdentityDuplicateException(
                postgresException.ConstraintName ?? string.Empty,
                exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.ForeignKeyViolation
            } postgresException)
        {
            throw new IdentityReferenceException(
                postgresException.ConstraintName ?? string.Empty,
                exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }

    private sealed class EfIdentityTransaction(IDbContextTransaction transaction) : IIdentityTransaction
    {
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            try { await transaction.CommitAsync(cancellationToken); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                throw new IdentityConcurrencyException(exception);
            }
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
