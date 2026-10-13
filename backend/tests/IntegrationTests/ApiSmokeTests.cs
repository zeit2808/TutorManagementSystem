using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TutorManagement.IntegrationTests;

public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
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
    public async Task Module_status_endpoints_return_standard_response(string route)
    {
        using var response = await _client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"success\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"traceId\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Module_endpoint_supports_xml_content_negotiation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/modules/academics");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Unsupported_accept_header_returns_not_acceptable()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/modules/academics");
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
}
