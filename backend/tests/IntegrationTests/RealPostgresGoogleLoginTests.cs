using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Google;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

public sealed class RealPostgresGoogleLoginTests
{
    private const string ValidToken = "valid-google-id-token-e2e";
    private const string InvalidToken = "invalid-google-id-token-e2e";
    private const string LinkToken = "link-google-id-token-e2e";

    [LocalPostgresFact]
    public async Task Google_login_issues_student_access_and_http_only_refresh_without_duplicate_user()
    {
        var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING") ??
            new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build()
                .GetConnectionString("MainDatabase") ??
            throw new InvalidOperationException("Configure a local test database.");
        var parsed = new NpgsqlConnectionStringBuilder(connectionString);
        if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase) ||
            parsed.Database != "tutor_management")
            throw new InvalidOperationException("This opt-in test only runs on local tutor_management.");
        var suffix = Guid.NewGuid().ToString("N");
        var email = $"e2e_google_{suffix}@gmail.com";
        var subject = $"e2e-google-{suffix}";
        var linkedEmail = $"e2e_google_link_{suffix}@example.invalid";
        var linkedSubject = $"e2e-google-link-{suffix}";
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:MainDatabase", connectionString);
            builder.UseSetting("Jwt:Issuer", "TutorManagement.GoogleE2E");
            builder.UseSetting("Jwt:Audience", "TutorManagement.GoogleE2E");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
            builder.UseSetting("Authentication:RefreshCookie:Secure", "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGoogleIdentityVerifier>();
                services.AddSingleton<IGoogleIdentityVerifier>(new StubGoogleVerifier(subject, email,
                    linkedSubject, linkedEmail));
            });
        });

        Guid userId = Guid.Empty;
        Guid linkedUserId = Guid.Empty;
        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var invalid = await client.PostAsJsonAsync("/api/v1/auth/google", new { idToken = InvalidToken });
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

            using var first = await client.PostAsJsonAsync("/api/v1/auth/google", new { idToken = ValidToken });
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var firstBody = await first.Content.ReadAsStringAsync();
            Assert.DoesNotContain("refreshToken\"", firstBody, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HttpOnly", first.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
            using var firstJson = JsonDocument.Parse(firstBody);
            var access = firstJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(access));

            using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/centers/{Guid.NewGuid()}/my/classes");
            authorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
            using var authorized = await client.SendAsync(authorizedRequest);
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

            using var second = await client.PostAsJsonAsync("/api/v1/auth/google", new { idToken = ValidToken });
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var users = await db.Users.Where(x => x.Email == email).ToArrayAsync();
            Assert.Single(users);
            userId = users[0].Id;
            Assert.Null(users[0].PasswordHash);
            Assert.Equal(1, await db.ExternalLogins.CountAsync(x => x.UserId == userId));

            const string currentPassword = "E2E-Link-Strong!123";
            var linkedUser = User.Create($"e2e_link_{suffix}", linkedEmail, "+84912345678",
                new PasswordService().Hash(currentPassword), DateTimeOffset.UtcNow);
            linkedUser.VerifyEmail(DateTimeOffset.UtcNow);
            linkedUser.AssignRole(SystemRoles.TeacherId);
            linkedUserId = linkedUser.Id;
            db.Users.Add(linkedUser);
            await db.SaveChangesAsync();

            using var passwordLogin = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { username = linkedUser.Username, password = currentPassword });
            Assert.Equal(HttpStatusCode.OK, passwordLogin.StatusCode);
            using var loginJson = JsonDocument.Parse(await passwordLogin.Content.ReadAsStringAsync());
            var localAccess = loginJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString();

            using var wrongLink = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/link")
            { Content = JsonContent.Create(new { idToken = LinkToken, currentPassword = "WrongPassword!123" }) };
            wrongLink.Headers.Authorization = new AuthenticationHeaderValue("Bearer", localAccess);
            using var denied = await client.SendAsync(wrongLink);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

            using var linkRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/google/link")
            { Content = JsonContent.Create(new { idToken = LinkToken, currentPassword }) };
            linkRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", localAccess);
            using var linked = await client.SendAsync(linkRequest);
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
            using var linkedGoogleLogin = await client.PostAsJsonAsync("/api/v1/auth/google", new { idToken = LinkToken });
            Assert.Equal(HttpStatusCode.OK, linkedGoogleLogin.StatusCode);
            Assert.Equal(1, await db.ExternalLogins.CountAsync(x => x.UserId == linkedUserId));

            // Linking adds a sign-in method only: the staff account does not become a student account.
            Assert.False(await db.Set<UserRole>().AnyAsync(x => x.UserId == linkedUserId && x.RoleId == SystemRoles.StudentId));
            using var linkedJson = JsonDocument.Parse(await linkedGoogleLogin.Content.ReadAsStringAsync());
            var linkedAccess = linkedJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
            using var studentOnly = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/centers/{Guid.NewGuid()}/my/classes");
            studentOnly.Headers.Authorization = new AuthenticationHeaderValue("Bearer", linkedAccess);
            using var forbidden = await client.SendAsync(studentOnly);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        finally
        {
            factory.Dispose();
            if (userId == Guid.Empty)
            {
                await using var lookup = new NpgsqlConnection(connectionString);
                await lookup.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "SELECT id FROM iam.users WHERE normalized_email = @email", lookup);
                command.Parameters.AddWithValue("email", email.ToUpperInvariant());
                userId = (Guid?)await command.ExecuteScalarAsync() ?? Guid.Empty;
            }
            if (userId != Guid.Empty || linkedUserId != Guid.Empty)
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var cleanup = new NpgsqlCommand("""
                    DELETE FROM iam.security_audit_events WHERE user_id IN (@user, @linked);
                    DELETE FROM iam.external_logins WHERE user_id IN (@user, @linked);
                    DELETE FROM iam.users WHERE (id = @user AND normalized_email = @email)
                        OR (id = @linked AND normalized_email = @linked_email);
                    """, connection);
                cleanup.Parameters.AddWithValue("user", userId);
                cleanup.Parameters.AddWithValue("linked", linkedUserId);
                cleanup.Parameters.AddWithValue("email", email.ToUpperInvariant());
                cleanup.Parameters.AddWithValue("linked_email", linkedEmail.ToUpperInvariant());
                await cleanup.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class StubGoogleVerifier(string subject, string email, string linkedSubject,
        string linkedEmail) : IGoogleIdentityVerifier
    {
        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken token) =>
            Task.FromResult<GoogleIdentity?>(idToken switch
            {
                ValidToken => new GoogleIdentity(subject, email, true, null),
                LinkToken => new GoogleIdentity(linkedSubject, linkedEmail, true, null),
                _ => null
            });
    }
}
