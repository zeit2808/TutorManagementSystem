using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TutorManagement.Modules.Academics.Application.Subjects.Abstractions;
using TutorManagement.Modules.Academics.Domain.Subjects;
using TutorManagement.Modules.IdentityAccess.Application.Centers.Abstractions;
using TutorManagement.Modules.IdentityAccess.Application.Authentication.Abstractions;
using TutorManagement.Modules.IdentityAccess.Domain.Authentication;
using TutorManagement.Modules.Organizations.Contracts.Centers;
using TutorManagement.Modules.Organizations.Application.Centers.Abstractions;
using TutorManagement.Modules.Organizations.Application.Rooms.Abstractions;
using TutorManagement.Modules.Organizations.Domain.Rooms;

namespace TutorManagement.IntegrationTests;

public sealed class CenterAccessApiTests : IClassFixture<CenterAccessApiTests.Factory>
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid ManagerId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid TeacherId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid ApprovedCenterId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SuspendedCenterId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid OtherCenterId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly byte[] SigningKey = new byte[64];
    private readonly HttpClient _client;

    public CenterAccessApiTests(Factory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task My_centers_requires_authentication()
    {
        using var response = await _client.GetAsync("/api/v1/me/centers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task My_centers_lists_only_active_memberships_and_marks_suspended_center_inaccessible()
    {
        using var request = AuthorizedRequest(OwnerId, "/api/v1/me/centers");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var centers = body.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(2, centers.Length);
        Assert.Contains(centers, center =>
            center.GetProperty("centerId").GetGuid() == ApprovedCenterId &&
            center.GetProperty("canAccess").GetBoolean() &&
            center.GetProperty("role").GetString() == "CenterOwner");
        Assert.Contains(centers, center =>
            center.GetProperty("centerId").GetGuid() == SuspendedCenterId &&
            !center.GetProperty("canAccess").GetBoolean() &&
            center.GetProperty("status").GetString() == "Suspended");
    }

    [Fact]
    public async Task Center_context_allows_member_of_approved_center()
    {
        using var request = AuthorizedRequest(
            OwnerId, $"/api/v1/centers/{ApprovedCenterId}/context");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApprovedCenterId, body.RootElement.GetProperty("data")
            .GetProperty("centerId").GetGuid());
    }

    [Theory]
    [InlineData("33333333-3333-4333-8333-333333333333")]
    [InlineData("44444444-4444-4444-8444-444444444444")]
    public async Task Center_context_denies_suspended_or_unrelated_center(string centerId)
    {
        using var request = AuthorizedRequest(OwnerId, $"/api/v1/centers/{centerId}/context");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":false", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Global_admin_without_center_membership_is_not_a_center_member()
    {
        using var request = AuthorizedRequest(
            Guid.NewGuid(), $"/api/v1/centers/{ApprovedCenterId}/context", "PlatformAdmin");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Center_context_without_token_is_unauthorized()
    {
        using var response = await _client.GetAsync($"/api/v1/centers/{ApprovedCenterId}/context");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unrelated_owner_cannot_read_center_profile()
    {
        using var request = AuthorizedRequest(
            OwnerId, $"/api/v1/centers/{OtherCenterId}/profile");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Center_admin_cannot_request_center_change()
    {
        using var request = AuthorizedRequest(
            ManagerId, $"/api/v1/centers/{ApprovedCenterId}/change-requests");
        request.Method = HttpMethod.Post;
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Center_owner_cannot_review_legal_changes_as_platform_admin()
    {
        using var request = AuthorizedRequest(
            OwnerId, $"/api/v1/platform/centers/{ApprovedCenterId}/change-requests");
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Subjects_require_authentication_and_an_approved_center_membership()
    {
        using var anonymous = await _client.GetAsync($"/api/v1/centers/{ApprovedCenterId}/subjects");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var suspendedRequest = AuthorizedRequest(OwnerId, $"/api/v1/centers/{SuspendedCenterId}/subjects");
        using var suspended = await _client.SendAsync(suspendedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, suspended.StatusCode);

        using var unrelatedRequest = AuthorizedRequest(OwnerId, $"/api/v1/centers/{OtherCenterId}/subjects");
        using var unrelated = await _client.SendAsync(unrelatedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, unrelated.StatusCode);
    }

    [Fact]
    public async Task Rooms_require_authentication_and_an_approved_center_membership()
    {
        using var anonymous = await _client.GetAsync($"/api/v1/centers/{ApprovedCenterId}/rooms");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var suspendedRequest = AuthorizedRequest(OwnerId, $"/api/v1/centers/{SuspendedCenterId}/rooms");
        using var suspended = await _client.SendAsync(suspendedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, suspended.StatusCode);

        using var unrelatedRequest = AuthorizedRequest(OwnerId, $"/api/v1/centers/{OtherCenterId}/rooms");
        using var unrelated = await _client.SendAsync(unrelatedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, unrelated.StatusCode);
    }

    [Theory]
    [InlineData("subjects")]
    [InlineData("rooms")]
    public async Task Catalogs_support_xml_with_populated_responses(string catalog)
    {
        var path = $"/api/v1/centers/{ApprovedCenterId}/{catalog}";
        var code = "XML" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var create = AuthorizedRequest(OwnerId, path);
        create.Method = HttpMethod.Post;
        create.Content = catalog == "rooms"
            ? JsonContent.Create(new { code, name = code })
            : JsonContent.Create(new { code, name = code, description = "XML test" });
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var request = AuthorizedRequest(TeacherId, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Teacher_and_platform_admin_cannot_create_rooms()
    {
        using var teacherRequest = AuthorizedRequest(TeacherId, $"/api/v1/centers/{ApprovedCenterId}/rooms");
        teacherRequest.Method = HttpMethod.Post;
        using var teacher = await _client.SendAsync(teacherRequest);
        Assert.Equal(HttpStatusCode.Forbidden, teacher.StatusCode);

        using var adminRequest = AuthorizedRequest(Guid.NewGuid(),
            $"/api/v1/centers/{ApprovedCenterId}/rooms", "PlatformAdmin");
        adminRequest.Method = HttpMethod.Post;
        using var admin = await _client.SendAsync(adminRequest);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task Center_owner_can_create_update_archive_and_reactivate_room_through_api()
    {
        var code = "R" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var path = $"/api/v1/centers/{ApprovedCenterId}/rooms";
        using var create = AuthorizedRequest(OwnerId, path);
        create.Method = HttpMethod.Post;
        create.Content = JsonContent.Create(new { code, name = code, locationNote = "Floor 2" });
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("data").TryGetProperty("capacity", out _));
        var roomId = body.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        using var read = AuthorizedRequest(TeacherId, $"{path}/{roomId}");
        using var found = await _client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);

        using var duplicate = AuthorizedRequest(ManagerId, path);
        duplicate.Method = HttpMethod.Post;
        duplicate.Content = JsonContent.Create(new { code, name = "Another room" });
        using var duplicateResponse = await _client.SendAsync(duplicate);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Contains("room_code_already_exists", await duplicateResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var invalid = AuthorizedRequest(ManagerId, $"{path}/{roomId}");
        invalid.Method = HttpMethod.Put;
        invalid.Content = JsonContent.Create(new { code, name = code });
        using var invalidResponse = await _client.SendAsync(invalid);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);

        using var update = AuthorizedRequest(ManagerId, $"{path}/{roomId}");
        update.Method = HttpMethod.Put;
        update.Content = JsonContent.Create(new { code, name = code, locationNote = "Floor 3", expectedVersion = 0 });
        using var updated = await _client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using var stale = AuthorizedRequest(OwnerId, $"{path}/{roomId}/archive");
        stale.Method = HttpMethod.Post;
        stale.Content = JsonContent.Create(new { expectedVersion = 0 });
        using var staleResponse = await _client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var archive = AuthorizedRequest(OwnerId, $"{path}/{roomId}/archive");
        archive.Method = HttpMethod.Post;
        archive.Content = JsonContent.Create(new { expectedVersion = 1 });
        using var archived = await _client.SendAsync(archive);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var activeList = AuthorizedRequest(TeacherId, path);
        using var active = await _client.SendAsync(activeList);
        Assert.DoesNotContain(roomId.ToString(), await active.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var reactivate = AuthorizedRequest(ManagerId, $"{path}/{roomId}/reactivate");
        reactivate.Method = HttpMethod.Post;
        reactivate.Content = JsonContent.Create(new { expectedVersion = 2 });
        using var restored = await _client.SendAsync(reactivate);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task Teacher_member_cannot_create_subject_and_platform_admin_has_no_implicit_access()
    {
        using var teacherRequest = AuthorizedRequest(TeacherId, $"/api/v1/centers/{ApprovedCenterId}/subjects");
        teacherRequest.Method = HttpMethod.Post;
        using var teacher = await _client.SendAsync(teacherRequest);
        Assert.Equal(HttpStatusCode.Forbidden, teacher.StatusCode);

        using var adminRequest = AuthorizedRequest(Guid.NewGuid(),
            $"/api/v1/centers/{ApprovedCenterId}/subjects", "PlatformAdmin");
        adminRequest.Method = HttpMethod.Post;
        using var admin = await _client.SendAsync(adminRequest);
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
    }

    [Fact]
    public async Task Center_owner_can_create_read_archive_and_reactivate_subject_through_api()
    {
        var code = "MATH" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var path = $"/api/v1/centers/{ApprovedCenterId}/subjects";
        using var create = AuthorizedRequest(OwnerId, path);
        create.Method = HttpMethod.Post;
        create.Content = JsonContent.Create(new { code, name = "Toán", description = "Môn học thử" });
        using var created = await _client.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var subjectId = body.RootElement.GetProperty("data").GetProperty("id").GetGuid();

        using var read = AuthorizedRequest(TeacherId, $"{path}/{subjectId}");
        using var found = await _client.SendAsync(read);
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);

        using var missingVersion = AuthorizedRequest(ManagerId, $"{path}/{subjectId}");
        missingVersion.Method = HttpMethod.Put;
        missingVersion.Content = JsonContent.Create(new { code, name = "Toán nâng cao" });
        using var rejected = await _client.SendAsync(missingVersion);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var update = AuthorizedRequest(ManagerId, $"{path}/{subjectId}");
        update.Method = HttpMethod.Put;
        update.Content = JsonContent.Create(new { code, name = "Toán nâng cao", description = "Đã cập nhật", expectedVersion = 0 });
        using var updated = await _client.SendAsync(update);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Contains("Toán nâng cao", await updated.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var archive = AuthorizedRequest(OwnerId, $"{path}/{subjectId}/archive");
        archive.Method = HttpMethod.Post;
        archive.Content = JsonContent.Create(new { expectedVersion = 1 });
        using var archived = await _client.SendAsync(archive);
        Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        using var defaultList = AuthorizedRequest(TeacherId, path);
        using var active = await _client.SendAsync(defaultList);
        Assert.DoesNotContain(subjectId.ToString(), await active.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        using var reactivate = AuthorizedRequest(ManagerId, $"{path}/{subjectId}/reactivate");
        reactivate.Method = HttpMethod.Post;
        reactivate.Content = JsonContent.Create(new { expectedVersion = 2 });
        using var restored = await _client.SendAsync(reactivate);
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    private static HttpRequestMessage AuthorizedRequest(Guid userId, string path, string role = "Teacher")
    {
        var token = new JwtSecurityToken(
            issuer: "TutorManagement.Tests",
            audience: "TutorManagement.Tests",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Sid, ActiveTestSessionRepository.SessionId.ToString()),
                new Claim("role", role)
            ],
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(SigningKey), SecurityAlgorithms.HmacSha256));
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return request;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "TutorManagement.Tests");
            builder.UseSetting("Jwt:Audience", "TutorManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(SigningKey));
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "30");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAuthSessionRepository>();
                services.AddSingleton<IAuthSessionRepository, ActiveTestSessionRepository>();
                services.RemoveAll<ICenterMembershipReader>();
                services.AddSingleton<ICenterMembershipReader, FakeMembershipReader>();
                services.RemoveAll<IOrganizationCenterReader>();
                services.AddSingleton<IOrganizationCenterReader, FakeCenterReader>();
                services.RemoveAll<ISubjectRepository>();
                services.AddSingleton<ISubjectRepository, FakeSubjectRepository>();
                services.RemoveAll<ISubjectClassGuard>();
                services.AddSingleton<ISubjectClassGuard, FakeSubjectClassGuard>();
                services.RemoveAll<IAcademicsUnitOfWork>();
                services.AddSingleton<IAcademicsUnitOfWork, FakeAcademicsUnitOfWork>();
                services.RemoveAll<IRoomRepository>();
                services.AddSingleton<IRoomRepository, FakeRoomRepository>();
                services.RemoveAll<IRoomArchiveGuard>();
                services.AddSingleton<IRoomArchiveGuard, FakeRoomArchiveGuard>();
                services.RemoveAll<IOrganizationUnitOfWork>();
                services.AddSingleton<IOrganizationUnitOfWork, FakeOrganizationUnitOfWork>();
            });
        }
    }

    private sealed class FakeMembershipReader : ICenterMembershipReader
    {
        private static readonly CenterMembershipSnapshot[] Memberships =
        [
            new(ApprovedCenterId, CenterMemberRole.CenterOwner),
            new(SuspendedCenterId, CenterMemberRole.CenterOwner)
        ];

        private static readonly CenterMembershipSnapshot ManagerMembership =
            new(ApprovedCenterId, CenterMemberRole.CenterAdmin);

        private static readonly CenterMembershipSnapshot TeacherMembership =
            new(ApprovedCenterId, CenterMemberRole.Teacher);

        public Task<CenterMembershipSnapshot?> FindActiveAsync(
            Guid userId, Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult(userId == OwnerId
                ? Memberships.FirstOrDefault(member => member.CenterId == centerId)
                : userId == ManagerId && centerId == ApprovedCenterId
                    ? ManagerMembership
                    : userId == TeacherId && centerId == ApprovedCenterId
                        ? TeacherMembership
                        : null);

        public Task<IReadOnlyCollection<CenterMembershipSnapshot>> ListActiveAsync(
            Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CenterMembershipSnapshot>>(
                userId == OwnerId ? Memberships
                    : userId == ManagerId ? [ManagerMembership]
                    : userId == TeacherId ? [TeacherMembership] : []);
    }

    private sealed class FakeCenterReader : IOrganizationCenterReader
    {
        private static readonly CenterAccessSnapshot[] Centers =
        [
            new(ApprovedCenterId, "Approved Center", true, "Approved"),
            new(SuspendedCenterId, "Suspended Center", false, "Suspended"),
            new(OtherCenterId, "Other Center", true, "Approved")
        ];

        public Task<CenterAccessSnapshot?> FindAsync(Guid centerId, CancellationToken cancellationToken) =>
            Task.FromResult(Centers.FirstOrDefault(center => center.Id == centerId));

        public Task<IReadOnlyCollection<CenterAccessSnapshot>> ListByIdsAsync(
            IReadOnlyCollection<Guid> centerIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<CenterAccessSnapshot>>(
                Centers.Where(center => centerIds.Contains(center.Id)).ToArray());
    }

    private sealed class FakeSubjectClassGuard : ISubjectClassGuard
    {
        public Task<ISubjectClassTransaction> BeginAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
            Task.FromResult<ISubjectClassTransaction>(new NoOpTransaction());

        public Task<bool> HasActiveClassesAsync(Guid centerId, Guid subjectId, CancellationToken token) =>
            Task.FromResult(false);

        private sealed class NoOpTransaction : ISubjectClassTransaction
        {
            public Task CommitAsync(CancellationToken token) => Task.CompletedTask;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSubjectRepository : ISubjectRepository
    {
        private readonly Dictionary<Guid, Subject> _subjects = [];

        public Task<Subject?> FindAsync(Guid centerId, Guid subjectId, CancellationToken cancellationToken) =>
            Task.FromResult(_subjects.TryGetValue(subjectId, out var subject) && subject.CenterId == centerId
                ? subject : null);

        public Task<IReadOnlyList<Subject>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Subject>>(_subjects.Values
                .Where(subject => subject.CenterId == centerId && (includeArchived || subject.IsActive)).ToArray());

        public Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_subjects.Values.Any(subject =>
                subject.CenterId == centerId && subject.NormalizedCode == normalizedCode && subject.Id != excludingId));

        public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_subjects.Values.Any(subject =>
                subject.CenterId == centerId && subject.NormalizedName == normalizedName && subject.Id != excludingId));

        public Task AddAsync(Subject subject, CancellationToken cancellationToken)
        {
            _subjects.Add(subject.Id, subject);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAcademicsUnitOfWork : IAcademicsUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
    }

    private sealed class FakeRoomRepository : IRoomRepository
    {
        private readonly Dictionary<Guid, Room> _rooms = [];

        public Task<Room?> FindAsync(Guid centerId, Guid roomId, CancellationToken cancellationToken) =>
            Task.FromResult(_rooms.TryGetValue(roomId, out var room) && room.CenterId == centerId ? room : null);

        public Task<IReadOnlyList<Room>> ListAsync(Guid centerId, bool includeArchived, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Room>>(_rooms.Values
                .Where(room => room.CenterId == centerId && (includeArchived || room.IsActive)).ToArray());

        public Task<bool> CodeExistsAsync(Guid centerId, string normalizedCode, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_rooms.Values.Any(room =>
                room.CenterId == centerId && room.NormalizedCode == normalizedCode && room.Id != excludingId));

        public Task<bool> NameExistsAsync(Guid centerId, string normalizedName, Guid? excludingId,
            CancellationToken cancellationToken) => Task.FromResult(_rooms.Values.Any(room =>
                room.CenterId == centerId && room.NormalizedName == normalizedName && room.Id != excludingId));

        public Task AddAsync(Room room, CancellationToken cancellationToken)
        {
            _rooms.Add(room.Id, room);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOrganizationUnitOfWork : IOrganizationUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeRoomArchiveGuard : IRoomArchiveGuard, IRoomArchiveTransaction
    {
        public Task<IRoomArchiveTransaction> BeginAsync(Guid centerId, Guid roomId, CancellationToken token) =>
            Task.FromResult<IRoomArchiveTransaction>(this);
        public Task<bool> HasFutureSessionsAsync(Guid centerId, Guid roomId, DateTimeOffset now, CancellationToken token) =>
            Task.FromResult(false);
        public Task CommitAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
