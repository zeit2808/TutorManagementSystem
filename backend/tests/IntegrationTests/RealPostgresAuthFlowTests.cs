using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Authentication;
using TutorManagement.Modules.IdentityAccess.Infrastructure;
using TutorManagement.Modules.IdentityAccess.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

// Opt-in because these tests create and remove a dedicated account in the local PostgreSQL database.
public sealed class LocalPostgresFactAttribute : FactAttribute
{
    public LocalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING")) &&
            Environment.GetEnvironmentVariable("TM_RUN_LOCAL_E2E") != "1")
            Skip = "Select AuthE2E.runsettings in Visual Studio, or set TM_E2E_CONNECTION_STRING explicitly.";
    }
}

public sealed class RealPostgresAuthFlowTests
{
    [LocalPostgresFact]
    public async Task Login_refresh_change_logout_lock_and_password_recovery_work_with_real_postgres()
    {
        await using var environment = await LocalAuthEnvironment.CreateAsync();

        using var firstLogin = await environment.LoginAsync(environment.InitialPassword);
        var first = await LocalAuthEnvironment.ReadTokensAsync(firstLogin);
        using var secondLogin = await environment.LoginAsync(environment.InitialPassword);
        var second = await LocalAuthEnvironment.ReadTokensAsync(secondLogin);

        using (var authorized = await environment.GetMyCentersAsync(first.AccessToken))
            Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);

        using var refreshed = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: first.Cookie);
        var rotated = await LocalAuthEnvironment.ReadTokensAsync(refreshed);
        Assert.NotEqual(first.Cookie, rotated.Cookie);

        const string changedPassword = "NewStrongPass!456";
        using var changed = await environment.PostAsync("/api/v1/auth/password/change",
            new { currentPassword = environment.InitialPassword, newPassword = changedPassword, confirmPassword = changedPassword },
            rotated.AccessToken, rotated.Cookie);
        Assert.True(changed.StatusCode == HttpStatusCode.OK,
            $"Change password returned {(int)changed.StatusCode}: {await changed.Content.ReadAsStringAsync()}");
        var current = await LocalAuthEnvironment.ReadTokensAsync(changed);

        using (var currentAccess = await environment.GetMyCentersAsync(current.AccessToken))
            Assert.Equal(HttpStatusCode.OK, currentAccess.StatusCode);
        using (var otherAccess = await environment.GetMyCentersAsync(second.AccessToken))
            Assert.Equal(HttpStatusCode.Unauthorized, otherAccess.StatusCode);
        using (var otherRefresh = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: second.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, otherRefresh.StatusCode);
        using (var oldPassword = await environment.LoginAsync(environment.InitialPassword))
            Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        using var newLogin = await environment.LoginAsync(changedPassword);
        var newSession = await LocalAuthEnvironment.ReadTokensAsync(newLogin);
        using (var logout = await environment.PostAsync("/api/v1/auth/logout", cookie: newSession.Cookie))
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using (var loggedOutRefresh = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: newSession.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, loggedOutRefresh.StatusCode);
        using (var loggedOutAccess = await environment.GetMyCentersAsync(newSession.AccessToken))
            Assert.Equal(HttpStatusCode.Unauthorized, loggedOutAccess.StatusCode);

        using (var logoutAll = await environment.PostAsync("/api/v1/auth/logout-all",
                   accessToken: current.AccessToken, cookie: current.Cookie))
            Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);
        using (var allSessionsRevoked = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: current.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, allSessionsRevoked.StatusCode);

        using var beforeResetLogin = await environment.LoginAsync(changedPassword);
        var beforeReset = await LocalAuthEnvironment.ReadTokensAsync(beforeResetLogin);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await environment.LoginAsync("WrongStrongPass!2026");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            Assert.Contains("invalid_credentials", await wrong.Content.ReadAsStringAsync());
        }
        using (var blocked = await environment.LoginAsync(changedPassword))
            Assert.Equal(HttpStatusCode.Unauthorized, blocked.StatusCode);
        Assert.Equal(5, await environment.FailedAttemptCountAsync());

        using (var forgot = await environment.PostAsync("/api/v1/auth/password/forgot",
                   new { email = environment.Email }))
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var supersededOtp = await environment.ReadLatestResetOtpAsync();
        using (var cooldown = await environment.PostAsync("/api/v1/auth/password/forgot",
                   new { email = environment.Email }))
            Assert.Equal(HttpStatusCode.Accepted, cooldown.StatusCode);
        Assert.Equal(1, await environment.ResetOtpMessageCountAsync());

        await environment.BackdateLatestResetTokenAsync(TimeSpan.FromSeconds(61));
        using (var resend = await environment.PostAsync("/api/v1/auth/password/forgot",
                   new { email = environment.Email }))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        Assert.Equal(2, await environment.ResetOtpMessageCountAsync());
        var otp = await environment.ReadLatestResetOtpAsync();
        Assert.NotEqual(supersededOtp, otp);
        using (var oldCode = await environment.PostAsync("/api/v1/auth/password/reset",
                   new { email = environment.Email, otp = supersededOtp, newPassword = "AnotherStrong!456", confirmPassword = "AnotherStrong!456" }))
            Assert.Equal(HttpStatusCode.BadRequest, oldCode.StatusCode);
        using (var invalidOtp = await environment.PostAsync("/api/v1/auth/password/reset",
                   new { email = environment.Email, otp = "00000000", newPassword = "AnotherStrong!456", confirmPassword = "AnotherStrong!456" }))
            Assert.Equal(HttpStatusCode.BadRequest, invalidOtp.StatusCode);

        const string recoveredPassword = "AnotherStrong!456";
        using (var reset = await environment.PostAsync("/api/v1/auth/password/reset",
                   new { email = environment.Email, otp, newPassword = recoveredPassword, confirmPassword = recoveredPassword }))
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(0, await environment.FailedAttemptCountAsync());
        using (var oldSession = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: beforeReset.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, oldSession.StatusCode);
        using (var previousPassword = await environment.LoginAsync(changedPassword))
            Assert.Equal(HttpStatusCode.Unauthorized, previousPassword.StatusCode);

        using var recoveredLogin = await environment.LoginAsync(recoveredPassword);
        var recovered = await LocalAuthEnvironment.ReadTokensAsync(recoveredLogin);
        // Two tabs refreshing with the same cookie at the same moment: both succeed, nobody is signed out.
        var racing = await Task.WhenAll(
            environment.PostAsync("/api/v1/auth/refresh-token", cookie: recovered.Cookie),
            environment.PostAsync("/api/v1/auth/refresh-token", cookie: recovered.Cookie));
        foreach (var response in racing)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"Concurrent refresh returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }
        var raced = await LocalAuthEnvironment.ReadTokensAsync(racing[1]);
        foreach (var response in racing) response.Dispose();

        // A cookie rotated moments ago (response lost on reload) still works once more, inside the grace period.
        using var recoveredRefresh = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: raced.Cookie);
        var replacement = await LocalAuthEnvironment.ReadTokensAsync(recoveredRefresh);
        using (var lateTab = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: raced.Cookie))
            Assert.Equal(HttpStatusCode.OK, lateTab.StatusCode);
        Assert.Equal(0, await environment.AuditEventCountAsync(SecurityAuditEventTypes.RefreshReuseDetected));

        // Past the grace period, a rotated token coming back is reuse: the whole session is revoked.
        await environment.BackdateRotatedRefreshTokensAsync(
            RefreshTokenCommandHandler.RotationGracePeriod + TimeSpan.FromSeconds(5));
        using (var replay = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: recovered.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using (var revokedFamily = await environment.PostAsync("/api/v1/auth/refresh-token", cookie: replacement.Cookie))
            Assert.Equal(HttpStatusCode.Unauthorized, revokedFamily.StatusCode);

        using var corsRequest = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        corsRequest.Headers.Add("Origin", "http://localhost:5173");
        corsRequest.Headers.Add("Access-Control-Request-Method", "POST");
        using var cors = await environment.Client.SendAsync(corsRequest);
        Assert.Equal(HttpStatusCode.NoContent, cors.StatusCode);
        Assert.Equal("http://localhost:5173", cors.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", cors.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var auditEvents = await environment.AuditEventTypesAsync();
        Assert.Contains(SecurityAuditEventTypes.LoginSucceeded, auditEvents);
        Assert.Contains(SecurityAuditEventTypes.LoginFailed, auditEvents);
        Assert.Contains(SecurityAuditEventTypes.LoginTemporarilyBlocked, auditEvents);
        Assert.Contains(SecurityAuditEventTypes.PasswordChanged, auditEvents);
        Assert.Contains(SecurityAuditEventTypes.PasswordReset, auditEvents);
        Assert.Contains(SecurityAuditEventTypes.RefreshReuseDetected, auditEvents);
    }

    [LocalPostgresFact]
    public async Task Concurrent_failures_and_password_change_are_serialized_for_one_account()
    {
        await using var environment = await LocalAuthEnvironment.CreateAsync();
        using var initialLogin = await environment.LoginAsync(environment.InitialPassword);
        var active = await LocalAuthEnvironment.ReadTokensAsync(initialLogin);

        var failures = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => environment.LoginAsync("WrongStrongPass!2026")));
        foreach (var failure in failures)
        {
            using (failure) Assert.Equal(HttpStatusCode.Unauthorized, failure.StatusCode);
        }
        Assert.Equal(4, await environment.FailedAttemptCountAsync());

        environment.PasswordService.Arm(environment.InitialPassword);
        var racingLogin = Task.Run(() => environment.LoginAsync(environment.InitialPassword));
        try
        {
            await environment.PasswordService.WaitUntilBlockedAsync();
            const string newPassword = "RaceStrongPass!456";
            using var changed = await environment.PostAsync("/api/v1/auth/password/change",
                new { currentPassword = environment.InitialPassword, newPassword, confirmPassword = newPassword },
                active.AccessToken, active.Cookie);
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        }
        finally
        {
            environment.PasswordService.Release();
        }

        using (var staleLogin = await racingLogin)
            Assert.Equal(HttpStatusCode.Unauthorized, staleLogin.StatusCode);
        using var freshLogin = await environment.LoginAsync("RaceStrongPass!456");
        Assert.Equal(HttpStatusCode.OK, freshLogin.StatusCode);
        Assert.Equal(0, await environment.FailedAttemptCountAsync());

        using (var forgot = await environment.PostAsync("/api/v1/auth/password/forgot",
                   new { email = environment.Email }))
            Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        var expiredOtp = await environment.ReadLatestResetOtpAsync();
        await environment.ExpireLatestResetTokenAsync();
        using (var expired = await environment.PostAsync("/api/v1/auth/password/reset",
                   new { email = environment.Email, otp = expiredOtp, newPassword = "AnotherStrong!456", confirmPassword = "AnotherStrong!456" }))
            Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        await environment.BackdateLatestResetTokenAsync(TimeSpan.FromSeconds(61));
        using (var resend = await environment.PostAsync("/api/v1/auth/password/forgot",
                   new { email = environment.Email }))
            Assert.Equal(HttpStatusCode.Accepted, resend.StatusCode);
        var validOtp = await environment.ReadLatestResetOtpAsync();
        using (var reset = await environment.PostAsync("/api/v1/auth/password/reset",
                   new { email = environment.Email, otp = validOtp, newPassword = "AnotherStrong!456", confirmPassword = "AnotherStrong!456" }))
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
    }

    [LocalPostgresFact]
    public async Task Login_ip_limit_rejects_excess_requests_before_touching_account_state()
    {
        await using var environment = await LocalAuthEnvironment.CreateAsync(loginLimit: 3);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var invalid = await environment.PostAsync("/api/v1/auth/login",
                new { username = "not_a_real_account", password = environment.InitialPassword });
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        }
        using var limited = await environment.PostAsync("/api/v1/auth/login",
            new { username = "not_a_real_account", password = environment.InitialPassword });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("rate_limit_exceeded", await limited.Content.ReadAsStringAsync());
        Assert.True(limited.Headers.Contains("Retry-After"));
        Assert.Equal(0, await environment.AuditEventCountAsync());
    }

    private sealed class LocalAuthEnvironment : IAsyncDisposable
    {
        private readonly string _connectionString;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly Guid _userId;

        private LocalAuthEnvironment(string connectionString, WebApplicationFactory<Program> factory,
            HttpClient client, Guid userId, string username, string email, BlockingPasswordService passwordService)
        {
            _connectionString = connectionString;
            _factory = factory;
            Client = client;
            _userId = userId;
            Username = username;
            Email = email;
            PasswordService = passwordService;
        }

        public HttpClient Client { get; }
        public string Username { get; }
        public string Email { get; }
        public string InitialPassword { get; } = "OldStrongPass!123";
        public BlockingPasswordService PasswordService { get; }

        public static async Task<LocalAuthEnvironment> CreateAsync(int loginLimit = 30)
        {
            var connectionString = Environment.GetEnvironmentVariable("TM_E2E_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                var secrets = new ConfigurationBuilder().AddUserSecrets("TutorManagement.Api-LocalDevelopment").Build();
                connectionString = secrets.GetConnectionString("MainDatabase")
                    ?? throw new InvalidOperationException("API User Secrets do not contain MainDatabase.");
            }
            var parsed = new NpgsqlConnectionStringBuilder(connectionString);
            if (!new[] { "localhost", "127.0.0.1", "::1" }.Contains(parsed.Host, StringComparer.OrdinalIgnoreCase) ||
                parsed.Database != "tutor_management")
                throw new InvalidOperationException("This opt-in test may only use the local tutor_management database.");

            var passwordService = new BlockingPasswordService();
            var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("ConnectionStrings:MainDatabase", connectionString);
                builder.UseSetting("Jwt:Issuer", "TutorManagement.E2E");
                builder.UseSetting("Jwt:Audience", "TutorManagement.E2E");
                builder.UseSetting("Jwt:SigningKey", signingKey);
                builder.UseSetting("Jwt:AccessTokenMinutes", "15");
                builder.UseSetting("Jwt:RefreshTokenDays", "1");
                builder.UseSetting("Authentication:RefreshCookie:Secure", "false");
                builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
                builder.UseSetting("RateLimit:LoginPermitLimit", loginLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IPasswordService>();
                    services.AddSingleton<IPasswordService>(passwordService);
                });
            });

            HttpClient? client = null;
            Guid userId = Guid.Empty;
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var username = $"e2e_auth_{suffix}";
            var email = $"{username}@example.invalid";
            try
            {
                client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
                var user = User.Create(username, email, "+849" + RandomNumberGenerator.GetInt32(10000000, 99999999),
                    passwordService.Hash("OldStrongPass!123"), DateTimeOffset.UtcNow);
                user.VerifyEmail(DateTimeOffset.UtcNow);
                user.AssignRole(SystemRoles.TeacherId);
                userId = user.Id;
                db.Users.Add(user);
                await db.SaveChangesAsync();
                await factory.Services.WarmUpIdentityAccessAsync();
                return new LocalAuthEnvironment(connectionString, factory, client, userId, username, email, passwordService);
            }
            catch
            {
                client?.Dispose();
                factory.Dispose();
                passwordService.Dispose();
                if (userId != Guid.Empty) await CleanupAsync(connectionString, userId, email);
                throw;
            }
        }

        public Task<HttpResponseMessage> LoginAsync(string password) =>
            PostAsync("/api/v1/auth/login", new { username = Username, password });

        public async Task<HttpResponseMessage> PostAsync(string path, object? body = null,
            string? accessToken = null, string? cookie = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (accessToken is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            if (cookie is not null) request.Headers.Add("Cookie", cookie);
            return await Client.SendAsync(request);
        }

        public async Task<HttpResponseMessage> GetMyCentersAsync(string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/centers");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            return await Client.SendAsync(request);
        }

        public static async Task<(string AccessToken, string Cookie)> ReadTokensAsync(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var cookieHeader = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("tm.refresh=", StringComparison.Ordinal));
            Assert.Contains("httponly", cookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookieHeader, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/api/v1/auth", cookieHeader, StringComparison.OrdinalIgnoreCase);
            var cookie = cookieHeader.Split(';', 2)[0];
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            Assert.DoesNotContain("refreshToken\"", json, StringComparison.OrdinalIgnoreCase);
            var accessToken = document.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
            Assert.False(string.IsNullOrWhiteSpace(accessToken));
            return (accessToken!, cookie);
        }

        public async Task<int> FailedAttemptCountAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.LoginSecurityStates.AsNoTracking()
                .Where(state => state.UserId == _userId)
                .Select(state => state.FailedAttemptCount)
                .SingleAsync();
        }

        public async Task<string[]> AuditEventTypesAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.SecurityAuditEvents.AsNoTracking()
                .Where(audit => audit.UserId == _userId)
                .Select(audit => audit.EventType)
                .ToArrayAsync();
        }

        public async Task<int> AuditEventCountAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.SecurityAuditEvents.CountAsync(audit => audit.UserId == _userId);
        }

        public async Task<string> ReadLatestResetOtpAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var body = await db.EmailOutboxMessages.AsNoTracking()
                .Where(message => message.Recipient == Email && message.SecurityTokenId != null)
                .OrderByDescending(message => message.CreatedAtUtc)
                .Select(message => message.Body)
                .FirstAsync();
            var match = Regex.Match(body, @"(?<!\d)\d{8}(?!\d)");
            Assert.True(match.Success, "No reset OTP was found in the test account's pending outbox message.");
            return match.Value;
        }

        public async Task<int> ResetOtpMessageCountAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.EmailOutboxMessages.CountAsync(message =>
                message.Recipient == Email && message.SecurityTokenId != null);
        }

        public async Task BackdateLatestResetTokenAsync(TimeSpan age)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tokenId = await db.UserSecurityTokens.AsNoTracking()
                .Where(token => token.UserId == _userId && token.Purpose == UserSecurityTokenPurposes.PasswordReset)
                .OrderByDescending(token => token.CreatedAtUtc)
                .Select(token => token.Id)
                .FirstAsync();
            await db.UserSecurityTokens.Where(token => token.Id == tokenId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.CreatedAtUtc,
                    DateTimeOffset.UtcNow - age));
        }

        public async Task BackdateRotatedRefreshTokensAsync(TimeSpan age)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await db.RefreshTokens
                .Where(token => token.UserId == _userId && token.RevocationReason == RefreshTokenRevocationReasons.Rotated)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc,
                    DateTimeOffset.UtcNow - age));
        }

        public async Task<int> AuditEventCountAsync(string eventType)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            return await db.SecurityAuditEvents.CountAsync(item => item.UserId == _userId && item.EventType == eventType);
        }

        public async Task ExpireLatestResetTokenAsync()
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var tokenId = await db.UserSecurityTokens.AsNoTracking()
                .Where(token => token.UserId == _userId && token.Purpose == UserSecurityTokenPurposes.PasswordReset)
                .OrderByDescending(token => token.CreatedAtUtc)
                .Select(token => token.Id)
                .FirstAsync();
            await db.UserSecurityTokens.Where(token => token.Id == tokenId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.ExpiresAtUtc,
                    DateTimeOffset.UtcNow.AddSeconds(-1)));
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            _factory.Dispose();
            PasswordService.Dispose();
            await CleanupAsync(_connectionString, _userId, Email);
        }

        private static async Task CleanupAsync(string connectionString, Guid userId, string email)
        {
            if (!email.StartsWith("e2e_auth_", StringComparison.Ordinal) ||
                !email.EndsWith("@example.invalid", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to clean up a non-test account.");

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand("""
                DELETE FROM iam.email_outbox_messages WHERE recipient = @email;
                DELETE FROM iam.security_audit_events WHERE user_id = @user_id;
                DELETE FROM iam.users WHERE id = @user_id AND normalized_email = @normalized_email;
                """, connection, transaction);
            command.Parameters.AddWithValue("email", email);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("normalized_email", email.ToUpperInvariant());
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }

    private sealed class BlockingPasswordService : IPasswordService, IDisposable
    {
        private readonly PasswordService _inner = new();
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new(false);
        private string? _passwordToBlock;
        private int _armed;

        public string Hash(string password) => _inner.Hash(password);

        public PasswordVerification Verify(string passwordHash, string providedPassword)
        {
            if (providedPassword == _passwordToBlock && Interlocked.CompareExchange(ref _armed, 0, 1) == 1)
            {
                _entered.TrySetResult();
                if (!_release.Wait(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The login/password-change race test did not release verification.");
            }
            return _inner.Verify(passwordHash, providedPassword);
        }

        public void PerformDummyVerification(string providedPassword) => _inner.PerformDummyVerification(providedPassword);

        public void Arm(string password)
        {
            _passwordToBlock = password;
            Volatile.Write(ref _armed, 1);
        }

        public Task WaitUntilBlockedAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(15));

        public void Release() => _release.Set();

        public void Dispose() => _release.Dispose();
    }
}
