using System.Xml.Serialization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TutorManagement.Modules.Academics.Application.Subjects;
using TutorManagement.Modules.Academics.Application.Teaching;
using TutorManagement.Modules.Organizations.Application.Rooms;
using TutorManagement.WebApi.Extensions;
using TutorManagement.WebApi.Responses;

namespace TutorManagement.IntegrationTests;

public sealed class TeachingResponseSerializationTests
{
    [Fact]
    public void List_responses_support_xml_formatter_without_406()
    {
        var page = new TeachingPage<StudentDto>
        {
            Page = 1,
            PageSize = 25,
            TotalCount = 1,
            Items = [new StudentDto { Id = Guid.NewGuid(), Code = "ST1", FullName = "Test Student" }]
        };
        var response = ApiResponse<TeachingPage<StudentDto>>.Ok(page, "test-trace");

        var serializer = new XmlSerializer(typeof(ApiResponse<TeachingPage<StudentDto>>));
        using var writer = new StringWriter();
        serializer.Serialize(writer, response);

        Assert.Contains("Test Student", writer.ToString());
        Assert.Contains("TotalCount", writer.ToString());
    }

    [Fact]
    public void Session_and_enrollment_responses_support_xml_formatter()
    {
        var enrollment = ApiResponse<List<EnrollmentDto>>.Ok(
            [new EnrollmentDto { Id = Guid.NewGuid(), EnrolledAtUtc = DateTimeOffset.UtcNow }], "trace");
        var session = ApiResponse<List<SessionDto>>.Ok(
            [new SessionDto { Id = Guid.NewGuid(), StartUtc = DateTimeOffset.UtcNow,
                EndUtc = DateTimeOffset.UtcNow.AddHours(1) }], "trace");

        Assert.Contains("EnrolledAtUtc", Serialize(enrollment));
        Assert.Contains("StartUtc", Serialize(session));
        var roster = ApiResponse<TeachingPage<ClassStudentDto>>.Ok(new TeachingPage<ClassStudentDto>
        {
            Page = 1, PageSize = 25, TotalCount = 1,
            Items = [new ClassStudentDto { StudentCode = "ST1", FullName = "Test Student", Status = "Active" }]
        }, "trace");
        var serializer = new XmlSerializer(roster.GetType());
        using var writer = new StringWriter();
        serializer.Serialize(writer, roster);
        Assert.Contains("Test Student", writer.ToString());
    }

    [Fact]
    public void Json_contract_omits_internal_fields_and_nulls_but_preserves_false_zero_and_empty_lists()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWebApiFoundation();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
            .Value.JsonSerializerOptions;

        object[] responses =
        [
            new SubjectDto { Name = "Math", IsActive = false, Version = 0 },
            new RoomDto { Name = "Room A", IsActive = false, Version = 0 },
            new ClassDto { Name = "Math class", IsActive = false, Version = 0 },
            new StudentDto { FullName = "Student", IsActive = false, Version = 0 },
            new TeacherDto { FullName = "Teacher", IsActive = false, Version = 0 }
        ];
        foreach (var response in responses)
        {
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, response.GetType(), options));
            Assert.False(json.RootElement.TryGetProperty("centerId", out _));
            Assert.False(json.RootElement.TryGetProperty("userId", out _));
            Assert.False(json.RootElement.TryGetProperty("createdByUserId", out _));
            Assert.False(json.RootElement.TryGetProperty("archivedByUserId", out _));
            Assert.False(json.RootElement.GetProperty("isActive").GetBoolean());
            Assert.Equal(0, json.RootElement.GetProperty("version").GetInt32());
            Assert.DoesNotContain(json.RootElement.EnumerateObject(), x => x.Value.ValueKind == JsonValueKind.Null);
        }

        var empty = ApiResponse<TeachingPage<StudentDto>>.Ok(new TeachingPage<StudentDto>
        {
            Page = 1, PageSize = 25, TotalCount = 0, Items = []
        }, "trace");
        using var page = JsonDocument.Parse(JsonSerializer.Serialize(empty, options));
        Assert.Equal(0, page.RootElement.GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Empty(page.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.False(page.RootElement.TryGetProperty("errors", out _));
    }

    private static string Serialize<T>(ApiResponse<List<T>> response)
    {
        var serializer = new XmlSerializer(typeof(ApiResponse<List<T>>));
        using var writer = new StringWriter();
        serializer.Serialize(writer, response);
        return writer.ToString();
    }
}
