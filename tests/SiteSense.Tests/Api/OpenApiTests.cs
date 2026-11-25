using System.Net;
using System.Text.Json;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class OpenApiTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;

    public OpenApiTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetOpenApi_Production_ReturnsDocument()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/openapi/v1.json");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _));
    }

    [Fact]
    public async Task GetScalar_Production_ReturnsPage()
    {
        // Arrange
        // Act
        var response = await _client.GetAsync("/scalar/v1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetOpenApi_CtorParametersWithoutDefault_AreRequired()
    {
        // Arrange
        // Act
        var body = await _client.GetStringAsync("/openapi/v1.json");

        // Assert
        using var doc = JsonDocument.Parse(body);
        var required = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ProbePoint").GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "lat", "lon" }, required.OrderBy(n => n).ToArray());
    }

    [Fact]
    public async Task GetOpenApi_Production_ListsSitesPaths()
    {
        // Arrange
        // Act
        var body = await _client.GetStringAsync("/openapi/v1.json");

        // Assert
        using var doc = JsonDocument.Parse(body);
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/sites", out _));
        Assert.True(paths.TryGetProperty("/api/v1/sites/{siteId}", out _));
    }
}
