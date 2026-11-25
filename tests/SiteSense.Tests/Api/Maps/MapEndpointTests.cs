using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Maps;

[Collection("Api")]
public sealed class MapEndpointTests : IClassFixture<ApiFactory>
{
    private const string MapUrl = "/api/v1/sites/site-01/map";
    private const string GeoJson = "application/geo+json";

    private readonly ApiFactory _factory;

    public MapEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Put_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(Skip = "needs SS-14 sites API")]
    public async Task Put_ValidMap_Returns200WithMapVersion()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        // Arrange: create site-01 for the token's tenant once SS-14 exists.
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("mapVersion").GetInt64());
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact(Skip = "needs SS-14 sites API")]
    public async Task Put_InvalidMap_Returns422WithErrors()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        // Arrange: create site-01 for the token's tenant once SS-14 exists.
        var invalid = MapFixtures.BaseMap.Replace("\"areaType\": \"perimeter\"", "\"areaType\": \"work\"");
        var content = new StringContent(invalid, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").EnumerateArray()
            .Select(e => (ElementId: e.GetProperty("elementId").ValueKind == JsonValueKind.Null ? null : e.GetProperty("elementId").GetString(),
                          Reason: e.GetProperty("reason").GetString()))
            .ToList();
        Assert.Contains(errors, e => e.ElementId is null && e.Reason == "perimeter missing");
    }

    [Fact(Skip = "needs SS-14 sites API")]
    public async Task Put_StaleIfMatch_Returns412()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        // Arrange: create site-01 for the token's tenant once SS-14 exists.
        var first = await client.PutAsync(MapUrl, new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = new HttpRequestMessage(HttpMethod.Put, MapUrl)
        {
            Content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson),
            Headers = { IfMatch = { new EntityTagHeaderValue("\"1\"") } },
        };
        var secondResponse = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        // Act: "1" is now stale, the stored version is 2.
        var third = new HttpRequestMessage(HttpMethod.Put, MapUrl)
        {
            Content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson),
            Headers = { IfMatch = { new EntityTagHeaderValue("\"1\"") } },
        };
        var response = await client.SendAsync(third);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact(Skip = "needs SS-14 sites API")]
    public async Task Put_OtherTenantsSite_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        // Arrange: create site-01 in tenant-b once SS-14 exists.
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl, content);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
