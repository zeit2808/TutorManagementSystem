using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.GetCurrentUser;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;

namespace TutorManagement.IntegrationTests;

public sealed class RealPostgresCurrentUserTests
{
    [LocalPostgresFact]
    public async Task Current_user_reports_roles_and_linked_sign_in_methods_from_the_database()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var options = new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options;
        var now = DateTimeOffset.UtcNow;
        var user = User.CreateExternal($"me-{Guid.NewGuid():N}@example.invalid", now);
        user.AssignRole(SystemRoles.StudentId);

        try
        {
            await using (var db = new IdentityDbContext(options))
            {
                db.Users.Add(user);
                db.ExternalLogins.Add(ExternalLogin.CreateGoogle(user.Id, $"google-{Guid.NewGuid():N}", now));
                await db.SaveChangesAsync();
            }

            await using (var db = new IdentityDbContext(options))
            {
                var handler = new GetCurrentUserQueryHandler(new CurrentUserReader(db));
                var result = await handler.HandleAsync(new GetCurrentUserQuery(user.Id), default);
                Assert.True(result.IsSuccess);
                Assert.Equal(user.Email, result.Value.Email);
                Assert.Equal([SystemRoles.Student], result.Value.Roles);
                Assert.True(result.Value.HasGoogle);
                Assert.False(result.Value.HasPassword);

                var missing = await handler.HandleAsync(new GetCurrentUserQuery(Guid.NewGuid()), default);
                Assert.Equal(CurrentUserErrors.NotFound, missing.Error);
            }
        }
        finally
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var cleanup = new NpgsqlCommand("""
                DELETE FROM iam.external_logins WHERE user_id = @user;
                DELETE FROM iam.user_roles WHERE user_id = @user;
                DELETE FROM iam.users WHERE id = @user;
                """, connection);
            cleanup.Parameters.AddWithValue("user", user.Id);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
