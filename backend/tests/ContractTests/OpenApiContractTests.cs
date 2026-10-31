using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TutorManagement.ContractTests;

/// <summary>
/// Guards the HTTP contract the front-end depends on.
/// <para>
/// The snapshot test fails whenever the generated OpenAPI document changes. When the change is
/// intended, regenerate with <c>TM_UPDATE_CONTRACT_SNAPSHOT=1 dotnet test tests/ContractTests</c>,
/// review the diff of <c>Snapshots/openapi.v1.json</c> and update the front-end types in the same change.
/// </para>
/// </summary>
public sealed class OpenApiContractTests(OpenApiContractTests.Factory factory) : IClassFixture<OpenApiContractTests.Factory>
{
    private const string Prefix = "TutorManagement_Modules_Academics_Application_Teaching_";

    [Fact]
    public async Task OpenApi_document_matches_reviewed_snapshot()
    {
        var current = await LoadDocumentAsync();
        var path = SnapshotPath();
        var normalized = current.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";

        if (Environment.GetEnvironmentVariable("TM_UPDATE_CONTRACT_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, normalized);
            return;
        }

        Assert.True(File.Exists(path),
            $"Missing contract snapshot {path}. Run once with TM_UPDATE_CONTRACT_SNAPSHOT=1 and commit the file.");
        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        Assert.True(expected == normalized,
            "The OpenAPI contract changed. If intended, rerun with TM_UPDATE_CONTRACT_SNAPSHOT=1, " +
            "review the snapshot diff and update QuanLyDayThemFE/lib/api in the same change.");
    }

    [Theory]
    [InlineData("SessionDto", new[] { "classCode", "className", "teacherName", "roomName", "version" })]
    [InlineData("ScheduleSeriesDto", new[] { "classCode", "className", "teacherName", "roomName", "version" })]
    [InlineData("StudentSessionDto", new[] { "classCode", "className", "teacherName", "roomName" })]
    [InlineData("ClassApplicationDto", new[] { "classCode", "className", "status" })]
    [InlineData("AttendanceSheetDto", new[] { "classCode", "className", "actualTeacherName", "version" })]
    [InlineData("StudentDto", new[] { "code", "parentName", "parentPhone" })]
    [InlineData("ClassStudentDto", new[] { "withdrawalReason", "withdrawnByName" })]
    [InlineData("AttendanceRevisionDto", new[] { "actorName" })]
    [InlineData("SessionDto", new[] { "status", "cancellationReason", "notHeldReason", "makeupForSessionId" })]
    [InlineData("SessionDto", new[] { "previousStartUtc", "previousEndUtc" })]
    [InlineData("StudentSessionDto", new[] { "status", "statusReason", "isRescheduled" })]
    [InlineData("MyScheduleEntryDto", new[] { "centerName", "role", "status", "overlapsWith", "previousStartUtc", "absenceRequestStatus" })]
    [InlineData("ScheduleCheckDto", new[] { "sameCenterConflicts", "otherCenterConflictCount" })]
    [InlineData("ScheduleChangeDto", new[] { "kind", "oldStartUtc", "newStartUtc", "reason", "isUrgent", "responses" })]
    [InlineData("StudentRequestDto", new[] { "kind", "status", "studentName", "sessionStartUtc", "targetClassName", "version" })]
    [InlineData("MyNoticeDto", new[] { "centerName", "conflictCount", "seenAtUtc", "response" })]
    [InlineData("AttendanceStudentDto", new[] { "excusedAbsenceApproved" })]
    public async Task Read_models_expose_display_names(string schema, string[] properties)
    {
        var names = PropertyNames(await LoadDocumentAsync(), Prefix + schema);
        foreach (var property in properties) Assert.Contains(property, names);
    }

    [Fact]
    public async Task Registration_toggle_requires_expected_version()
    {
        var names = PropertyNames(await LoadDocumentAsync(),
            "TutorManagement_Modules_Academics_Contracts_Teaching_SetClassRegistrationRequest");
        Assert.Contains("open", names);
        Assert.Contains("expectedVersion", names);
    }

    [Theory]
    [InlineData("/me/schedule")]
    [InlineData("/me/notices")]
    [InlineData("/me/requests")]
    [InlineData("/class-applications/check")]
    [InlineData("/sessions/impact")]
    [InlineData("/student-requests")]
    [InlineData("/schedule-changes")]
    public async Task Schedule_rule_endpoints_are_documented(string suffix)
    {
        var document = await LoadDocumentAsync();
        Assert.Contains(document["paths"]!.AsObject().Select(x => x.Key), x => x.EndsWith(suffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Me_endpoint_is_documented()
    {
        var document = await LoadDocumentAsync();
        var paths = document["paths"]!.AsObject().Select(x => x.Key).ToArray();
        Assert.Contains(paths, x => x.EndsWith("/me", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/v1/centers/{centerId}/members")]
    [InlineData("/api/v1/centers/{centerId}/member-invitations")]
    [InlineData("/api/v1/centers/{centerId}/members/{membershipId}/role")]
    [InlineData("/api/v1/me/invitations/accept")]
    [InlineData("/api/v1/auth/invitations/preview")]
    [InlineData("/api/v1/centers/{centerId}/teachers/{teacherId}/account")]
    [InlineData("/api/v1/centers/{centerId}/my/teaching")]
    [InlineData("/api/v1/centers/{centerId}/classes/{classId}/enrollments/{studentId}/withdraw")]
    [InlineData("/api/v1/platform/centers/{centerId}/resubmit")]
    [InlineData("/api/v1/platform/centers/{centerId}/history")]
    [InlineData("/api/v1/platform/change-requests")]
    [InlineData("/api/v1/platform/centers/{centerId}")]
    [InlineData("/api/v1/platform/center-counts")]
    [InlineData("/api/v1/platform/centers/{centerId}/invitations/{invitationId}/resend")]
    [InlineData("/api/v1/centers/{centerId}/directory")]
    [InlineData("/api/v1/centers/{centerId}/status-history")]
    [InlineData("/api/v1/centers/{centerId}/change-requests/{requestId}/withdraw")]
    [InlineData("/api/v1/centers/{centerId}/member-invitations/{invitationId}/resend")]
    [InlineData("/api/v1/centers/{centerId}/sessions/{sessionId}/not-held")]
    [InlineData("/api/v1/directory/centers")]
    [InlineData("/api/v1/directory/centers/{centerId}")]
    [InlineData("/api/v1/directory/subjects")]
    public async Task Membership_and_teacher_endpoints_are_documented(string path)
    {
        var document = await LoadDocumentAsync();
        Assert.True(document["paths"]?[path] is not null, $"{path} is missing from the OpenAPI document.");
    }

    private async Task<JsonNode> LoadDocumentAsync()
    {
        using var client = factory.CreateClient();
        var json = await client.GetStringAsync("/swagger/v1/swagger.json");
        return Sort(JsonNode.Parse(json)!);
    }

    private static HashSet<string> PropertyNames(JsonNode document, string schema)
    {
        var node = document["components"]?["schemas"]?[schema];
        Assert.True(node is not null, $"Schema {schema} is missing from the OpenAPI document.");
        return node!["properties"]?.AsObject().Select(x => x.Key).ToHashSet(StringComparer.Ordinal) ?? [];
    }

    /// <summary>Orders object keys so the snapshot only changes when the contract does.</summary>
    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => KeyValuePair.Create(x.Key, x.Value is null ? null : Sort(x.Value.DeepClone())))),
        JsonArray array => new JsonArray(array.Select(x => x is null ? null : Sort(x.DeepClone())).ToArray()),
        _ => node.DeepClone()
    };

    private static string SnapshotPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Snapshots", "openapi.v1.json");

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Jwt:Issuer", "TutorManagement.Tests");
            builder.UseSetting("Jwt:Audience", "TutorManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[64]));
            builder.UseSetting("Jwt:AccessTokenMinutes", "15");
            builder.UseSetting("Jwt:RefreshTokenDays", "30");
            builder.UseSetting("Cors:AllowedOrigins:0", "http://localhost:5173");
        }
    }
}
