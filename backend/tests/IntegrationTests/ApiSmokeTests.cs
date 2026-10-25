using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TutorManagement.Application.Abstractions.Messaging;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.IdentityAccess.Application.Authentication;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Login;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Logout;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Refresh;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Password;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.ResendVerificationEmail;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.VerifyEmail;
using TutorManagement.Modules.Organizations.Application.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Application.Centers.ListCenters;
using TutorManagement.Modules.Organizations.Domain.Centers;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly byte[] TestSigningKey = new byte[64];
    private static readonly string[] TokenFields = ["accessToken", "expiresIn", "tokenType"];
    private static readonly string[] AcknowledgementFields = ["message", "success", "traceId"];

    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting("Jwt:Issuer", "TutorManagement.Tests");
                builder.UseSetting("Jwt:Audience", "TutorManagement.Tests");
                builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(TestSigningKey));
                builder.UseSetting("Jwt:AccessTokenMinutes", "15");
                builder.UseSetting("Jwt:RefreshTokenDays", "30");
                builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IAuthSessionRepository>();
                    services.AddSingleton<IAuthSessionRepository, ActiveTestSessionRepository>();
                    services.RemoveAll<ICommandHandler<LoginCommand, AuthenticationResult>>();
                    services.RemoveAll<IQueryHandler<ListCentersQuery, List<CenterDto>>>();
                    services.AddSingleton<IQueryHandler<ListCentersQuery, List<CenterDto>>, EmptyCentersHandler>();
                    services.RemoveAll<ICenterRepository>();
                    services.AddSingleton<ICenterRepository, EmptyCenterRepository>();
                    services.AddSingleton<
                        ICommandHandler<LoginCommand, AuthenticationResult>,
                        SuccessfulLoginHandler>();
                    services.RemoveAll<ICommandHandler<RefreshTokenCommand, AuthenticationResult>>();
                    services.AddSingleton<
                        ICommandHandler<RefreshTokenCommand, AuthenticationResult>,
                        SuccessfulRefreshTokenHandler>();
                    services.RemoveAll<ICommandHandler<LogoutCommand>>();
                    services.AddSingleton<ICommandHandler<LogoutCommand>, SuccessfulLogoutHandler>();
                    services.RemoveAll<ICommandHandler<LogoutAllCommand>>();
                    services.AddSingleton<ICommandHandler<LogoutAllCommand>, SuccessfulLogoutAllHandler>();
                    services.RemoveAll<ICommandHandler<ChangePasswordCommand, AuthenticationResult>>();
                    services.AddSingleton<ICommandHandler<ChangePasswordCommand, AuthenticationResult>, SuccessfulChangePasswordHandler>();
                    services.RemoveAll<ICommandHandler<ForgotPasswordCommand>>();
                    services.AddSingleton<ICommandHandler<ForgotPasswordCommand>, SuccessfulForgotPasswordHandler>();
                    services.RemoveAll<ICommandHandler<ResetPasswordCommand>>();
                    services.AddSingleton<ICommandHandler<ResetPasswordCommand>, SuccessfulResetPasswordHandler>();
                    services.RemoveAll<ICommandHandler<VerifyEmailCommand>>();
                    services.AddSingleton<ICommandHandler<VerifyEmailCommand>, SuccessfulVerifyEmailHandler>();
                    services.RemoveAll<ICommandHandler<ResendVerificationEmailCommand>>();
                    services.AddSingleton<
                        ICommandHandler<ResendVerificationEmailCommand>,
                        SuccessfulResendVerificationEmailHandler>();
                });
            })
            .CreateClient();
    }

    [Fact]
    public async Task Liveness_endpoint_returns_ok()
    {
        using var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/modules/identity-access")]
    [InlineData("/api/v1/modules/organizations")]
    [InlineData("/api/v1/modules/academics")]
    public async Task Obsolete_module_status_endpoints_are_not_exposed(string route)
    {
        using var response = await _client.GetAsync(route);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_endpoint_supports_xml_content_negotiation()
    {
        using var request = CreateAuthorizedRequest("PlatformAdmin");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unsupported_accept_header_returns_not_acceptable()
    {
        using var request = CreateAuthorizedRequest("PlatformAdmin");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/csv"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotAcceptable, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_available_in_development()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_document_is_available_in_development()
    {
        using var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Tutor Management API", body, StringComparison.Ordinal);
        Assert.Contains("bearer", body, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/google", out var googleLogin));
        Assert.True(googleLogin.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/google/link", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/class-applications", out var applications));
        Assert.True(applications.TryGetProperty("get", out _));
        Assert.True(applications.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/class-applications/{applicationId}/approve", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/schedule-series", out var series));
        Assert.True(series.TryGetProperty("get", out _));
        Assert.True(series.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/schedule-series/preview", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/schedule-series/{seriesId}/stop", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/schedule-series/{seriesId}/replace-future", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}", out var session));
        Assert.True(session.TryGetProperty("put", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}/attendance", out var attendance));
        Assert.True(attendance.TryGetProperty("get", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}/attendance/draft", out var draft));
        Assert.True(draft.TryGetProperty("put", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}/attendance/submit", out var submit));
        Assert.True(submit.TryGetProperty("post", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}/attendance/correction", out var correction));
        Assert.True(correction.TryGetProperty("patch", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions/{sessionId}/attendance/history", out _));
        string[] getRoutes =
        [
            "/api/v1/platform/centers",
            "/api/v1/me/centers",
            "/api/v1/centers/{centerId}/profile"
        ];

        foreach (var route in getRoutes)
        {
            var responseContent = paths
                .GetProperty(route)
                .GetProperty("get")
                .GetProperty("responses")
                .GetProperty("200")
                .GetProperty("content");
            Assert.Equal("application/json", responseContent.EnumerateObject().First().Name);
        }

        Assert.False(paths.TryGetProperty("/api/v1/modules/academics", out _));
        Assert.False(paths.TryGetProperty("/api/v1/modules/organizations", out _));
        Assert.False(paths.TryGetProperty("/api/v1/modules/identity-access", out _));
        Assert.False(paths.TryGetProperty("/api/v1/auth/platform-admin-check", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/password/change", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/password/forgot", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/password/reset", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/classes", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/students", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/teachers", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/sessions", out _));
        Assert.True(paths.TryGetProperty("/api/v1/centers/{centerId}/classes/{classId}/enrollments", out _));

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (var type in new[] { typeof(SubjectDto), typeof(RoomDto), typeof(ClassDto),
                     typeof(StudentDto), typeof(TeacherDto), typeof(ClassApplicationDto) })
        {
            var properties = schemas.GetProperty(type.FullName!.Replace('.', '_')).GetProperty("properties");
            Assert.False(properties.TryGetProperty("centerId", out _));
        }
        var enrollmentSchema = paths.GetProperty("/api/v1/centers/{centerId}/classes/{classId}/enrollments")
            .GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString();
        Assert.Contains("TeachingPage", enrollmentSchema, StringComparison.Ordinal);
        Assert.Contains("ClassStudentDto", enrollmentSchema, StringComparison.Ordinal);

        Assert.False(paths.GetProperty("/api/v1/centers/{centerId}/profile").TryGetProperty("put", out _));
        Assert.False(paths.TryGetProperty("/api/v1/centers/{centerId}/contact-email-changes", out _));
        Assert.False(paths.TryGetProperty("/api/v1/centers/{centerId}/legal-change-requests", out _));
        Assert.Equal("04 · Center change requests", paths
            .GetProperty("/api/v1/centers/{centerId}/change-requests")
            .GetProperty("post").GetProperty("tags")[0].GetString());
        Assert.Equal("91 · Platform review", paths
            .GetProperty("/api/v1/platform/centers/{centerId}/change-requests/{requestId}/approve")
            .GetProperty("post").GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Staff_token_cannot_use_student_self_service_routes()
    {
        using var request = CreateAuthorizedRequest("Teacher", HttpMethod.Get,
            $"/api/v1/centers/{Guid.NewGuid()}/my/classes");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_ui_is_available_in_development()
    {
        using var response = await _client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Platform_admin_endpoint_without_access_token_returns_standard_unauthorized_response()
    {
        using var response = await _client.GetAsync("/api/v1/platform/centers");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.Ordinal);
        Assert.Contains("unauthorized", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.TryGetProperty("data", out _));
        Assert.False(document.RootElement.GetProperty("errors")[0].TryGetProperty("field", out _));
    }

    [Fact]
    public async Task Platform_admin_endpoint_with_platform_admin_role_returns_ok()
    {
        using var request = CreateAuthorizedRequest("PlatformAdmin");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"items\":[]", body, StringComparison.Ordinal);
        Assert.Contains("\"totalCount\":0", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoked_or_unknown_session_rejects_otherwise_valid_jwt()
    {
        using var request = CreateAuthorizedRequest("PlatformAdmin", sessionId: Guid.NewGuid());
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("unauthorized", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Platform_admin_endpoint_with_teacher_role_returns_standard_forbidden_response()
    {
        using var request = CreateAuthorizedRequest("Teacher");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.Ordinal);
        Assert.Contains("forbidden", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_invalid_payload_returns_standard_validation_response()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.Ordinal);
        Assert.Contains("validation_error", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_ip_rate_limit_returns_standard_429_response()
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            using var allowed = await _client.PostAsJsonAsync("/api/v1/auth/login", new { });
            Assert.Equal(HttpStatusCode.BadRequest, allowed.StatusCode);
        }

        using var rejected = await _client.PostAsJsonAsync("/api/v1/auth/login", new { });
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.Contains("Retry-After"));
        var body = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Successful_login_sets_hardened_refresh_token_cookie()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new
            {
                username = "cookie.test",
                password = "Valid-password-123!"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));

        var refreshCookie = Assert.Single(cookieHeaders);
        Assert.Contains("tm.refresh=integration-test-refresh-token", refreshCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("no-cache", response.Headers.Pragma.ToString(), StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.TryGetProperty("errors", out _));
        Assert.Equal(TokenFields,
            document.RootElement.GetProperty("data").EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(900, document.RootElement.GetProperty("data").GetProperty("expiresIn").GetInt32());
    }

    [Fact]
    public async Task Public_registration_endpoint_is_not_exposed()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new
            {
                username = "x",
                email = "invalid-email",
                phoneNumber = "123",
                password = "weak",
                confirmPassword = "different"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var swaggerResponse = await _client.GetAsync("/swagger/v1/swagger.json");
        using var swagger = JsonDocument.Parse(await swaggerResponse.Content.ReadAsStringAsync());
        Assert.False(
            swagger.RootElement
                .GetProperty("paths")
                .TryGetProperty("/api/v1/auth/register", out _));
    }

    [Fact]
    public async Task Verify_email_accepts_valid_one_time_token_contract()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/verify-email",
            new { token = "integration-test-email-verification-token" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Resend_verification_email_returns_no_content_to_prevent_account_enumeration()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/resend-verification-email",
            new { email = "unknown@example.com" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_reads_http_only_cookie_rotates_it_and_does_not_expose_token_in_json()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/refresh-token");
        request.Headers.Add("Cookie", "tm.refresh=integration-test-refresh-token");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var refreshCookie = Assert.Single(cookieHeaders);
        Assert.Contains("tm.refresh=rotated-integration-test-refresh-token", refreshCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("data").TryGetProperty("refreshToken", out _));
    }

    [Fact]
    public async Task Cors_preflight_allows_credentials_only_for_configured_frontend()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Options,
            "/api/v1/auth/refresh-token");
        request.Headers.Add("Origin", "http://localhost:5173");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "http://localhost:5173",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal(
            "true",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        Assert.Contains(
            "POST",
            Assert.Single(response.Headers.GetValues("Access-Control-Allow-Methods")),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_is_idempotent_and_expires_refresh_cookie()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        request.Headers.Add("Cookie", "tm.refresh=integration-test-refresh-token");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        var refreshCookie = Assert.Single(cookieHeaders);
        Assert.Contains("tm.refresh=", refreshCookie, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logout_all_requires_access_token_and_expires_refresh_cookie()
    {
        using var request = CreateAuthorizedRequest(
            "PlatformAdmin",
            HttpMethod.Post,
            "/api/v1/auth/logout-all");
        request.Headers.Add("Cookie", "tm.refresh=integration-test-refresh-token");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders));
        Assert.Contains(
            "expires=Thu, 01 Jan 1970",
            Assert.Single(cookieHeaders),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Forgot_password_returns_generic_accepted_response()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/forgot", new { email = "unknown@example.com" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("If the address belongs to an eligible account", body, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown@example.com", body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal(AcknowledgementFields,
            document.RootElement.EnumerateObject().Select(x => x.Name).Order().ToArray());
    }

    [Fact]
    public async Task Reset_password_rejects_malformed_otp_with_standard_validation_response()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = "user@example.com", otp = "abc", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("validation_error", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var missingOtp = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = "user@example.com", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.BadRequest, missingOtp.StatusCode);
    }

    [Fact]
    public async Task Reset_password_succeeds_without_issuing_tokens()
    {
        using var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { email = "user@example.com", otp = "12345678", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Change_password_requires_current_bearer_and_cookie()
    {
        using var unauthenticated = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/change",
            new { currentPassword = "OldPass!12345", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        using var request = CreateAuthorizedRequest("PlatformAdmin", HttpMethod.Post, "/api/v1/auth/password/change");
        request.Content = JsonContent.Create(new { currentPassword = "OldPass!12345", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        using var noCookie = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, noCookie.StatusCode);
    }

    [Fact]
    public async Task Change_password_rotates_cookie_without_exposing_refresh_token()
    {
        using var request = CreateAuthorizedRequest("PlatformAdmin", HttpMethod.Post, "/api/v1/auth/password/change");
        request.Headers.Add("Cookie", "tm.refresh=integration-test-refresh-token");
        request.Content = JsonContent.Create(new { currentPassword = "OldPass!12345", newPassword = "StrongPass!123", confirmPassword = "StrongPass!123" });
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("httponly", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("data").TryGetProperty("refreshToken", out _));
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        string role,
        HttpMethod? method = null,
        string path = "/api/v1/platform/centers",
        Guid? sessionId = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(TestSigningKey),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: "TutorManagement.Tests",
            audience: "TutorManagement.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Sid,
                    (sessionId ?? ActiveTestSessionRepository.SessionId).ToString()),
                new Claim("role", role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JwtSecurityTokenHandler().WriteToken(token));
        return request;
    }

    private sealed class EmptyCenterRepository : ICenterRepository
    {
        public Task<Center?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult<Center?>(null);
        public Task<bool> TaxCodeExistsAsync(string taxCode, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task AddAsync(Center center, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<Center>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<Center>>([]);
        public Task<bool> TaxCodeExistsForOtherAsync(string taxCode, Guid exceptCenterId,
            CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<(IReadOnlyList<Center> Items, int TotalCount)> ListPageAsync(CenterStatus? status,
            string? search, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Center>, int)>(([], 0));
        public Task<IReadOnlyDictionary<CenterStatus, int>> CountByStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<CenterStatus, int>>(new Dictionary<CenterStatus, int>());
        public Task<IReadOnlyList<CenterStatusChange>> ListHistoryAsync(Guid centerId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CenterStatusChange>>([]);
    }

    private sealed class EmptyCentersHandler : IQueryHandler<ListCentersQuery, List<CenterDto>>
    {
        public Task<Result<List<CenterDto>>> HandleAsync(
            ListCentersQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<List<CenterDto>>.Success([]));
    }

    private sealed class SuccessfulLoginHandler
        : ICommandHandler<LoginCommand, AuthenticationResult>
    {
        public Task<Result<AuthenticationResult>> HandleAsync(
            LoginCommand command,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(Result<AuthenticationResult>.Success(
                new AuthenticationResult(
                    "integration-test-access-token",
                    "integration-test-refresh-token",
                    900,
                    now.AddMinutes(15),
                    now.AddDays(30))));
        }
    }

    private sealed class SuccessfulRefreshTokenHandler
        : ICommandHandler<RefreshTokenCommand, AuthenticationResult>
    {
        public Task<Result<AuthenticationResult>> HandleAsync(
            RefreshTokenCommand command,
            CancellationToken cancellationToken)
        {
            if (!string.Equals(
                    command.RefreshToken,
                    "integration-test-refresh-token",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(Result<AuthenticationResult>.Failure(
                    new Error("invalid_refresh_token", "Refresh token is invalid.")));
            }

            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(Result<AuthenticationResult>.Success(
                new AuthenticationResult(
                    "rotated-integration-test-access-token",
                    "rotated-integration-test-refresh-token",
                    900,
                    now.AddMinutes(15),
                    now.AddDays(30))));
        }
    }

    private sealed class SuccessfulLogoutHandler : ICommandHandler<LogoutCommand>
    {
        public Task<Result> HandleAsync(
            LogoutCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulLogoutAllHandler : ICommandHandler<LogoutAllCommand>
    {
        public Task<Result> HandleAsync(
            LogoutAllCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulChangePasswordHandler : ICommandHandler<ChangePasswordCommand, AuthenticationResult>
    {
        public Task<Result<AuthenticationResult>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(Result<AuthenticationResult>.Success(new AuthenticationResult(
                "new-access-token", "new-refresh-token", 900, now.AddMinutes(15), now.AddDays(30))));
        }
    }

    private sealed class SuccessfulForgotPasswordHandler : ICommandHandler<ForgotPasswordCommand>
    {
        public Task<Result> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulResetPasswordHandler : ICommandHandler<ResetPasswordCommand>
    {
        public Task<Result> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulVerifyEmailHandler : ICommandHandler<VerifyEmailCommand>
    {
        public Task<Result> HandleAsync(
            VerifyEmailCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class SuccessfulResendVerificationEmailHandler
        : ICommandHandler<ResendVerificationEmailCommand>
    {
        public Task<Result> HandleAsync(
            ResendVerificationEmailCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }
}
