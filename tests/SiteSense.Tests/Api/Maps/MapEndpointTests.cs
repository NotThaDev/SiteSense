using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Maps;

[Collection("Api")]
public sealed class MapEndpointTests : IClassFixture<ApiFactory>
{
    private const string GeoJson = "application/geo+json";

    private readonly ApiFactory _factory;

    public MapEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    private static string MapUrl(string siteId) => $"/api/v1/sites/{siteId}/map";

    // Creating the host resets the static store, so seed after CreateClient.
    private static Task SeedSiteAsync(string tenantId, string siteId) =>
        SiteSenseContext.Store.SaveAsync(tenantId, new Site
        {
            Id = siteId,
            TenantId = tenantId,
            Name = "Test site",
            TimeZone = "Europe/Rome",
            Navigation = new NavigationSettings(1.3, 0.8, 0.5, 0.5, 30),
        }, CancellationToken.None);

    [Fact]
    public async Task Put_WithoutToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-401"), content);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ValidMap_Returns200WithMapVersion()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-ok");
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-ok"), content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("mapVersion").GetInt64());
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_InvalidMap_Returns422WithErrors()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-422");
        var invalid = MapFixtures.BaseMap.Replace("\"areaType\": \"perimeter\"", "\"areaType\": \"work\"");
        var content = new StringContent(invalid, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-422"), content);

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

    [Fact]
    public async Task Put_StaleIfMatch_Returns412()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-412");
        var first = await client.PutAsync(MapUrl("site-412"), new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = new HttpRequestMessage(HttpMethod.Put, MapUrl("site-412"))
        {
            Content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson),
            Headers = { IfMatch = { new EntityTagHeaderValue("\"1\"") } },
        };
        var secondResponse = await client.SendAsync(second);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        // Act: "1" is now stale, the stored version is 2.
        var third = new HttpRequestMessage(HttpMethod.Put, MapUrl("site-412"))
        {
            Content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson),
            Headers = { IfMatch = { new EntityTagHeaderValue("\"1\"") } },
        };
        var response = await client.SendAsync(third);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Put_OtherTenantsSite_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-b", "site-iso");
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-iso"), content);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_ViewerRole_Returns403()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Viewer"]));
        await SeedSiteAsync("tenant-a", "site-403");
        var content = new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-403"), content);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_RealSiteMap_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-real");
        var content = new StringContent(MapFixtures.RealMap, Encoding.UTF8, GeoJson);

        // Act
        var response = await client.PutAsync(MapUrl("site-real"), content);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("mapVersion").GetInt64());
    }

    private static HttpRequestMessage PutMap(string siteId, string? ifMatchRaw, string body = MapFixtures.BaseMap)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, MapUrl(siteId)) { Content = new StringContent(body, Encoding.UTF8, GeoJson) };
        if (ifMatchRaw is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatchRaw);
        return request;
    }

    private static async Task<long> VersionOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("mapVersion").GetInt64();
    }

    [Fact]
    public async Task Put_FirstMapWithoutIfMatch_Returns200Version1()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-first");

        // Act
        var response = await client.SendAsync(PutMap("site-first", null));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await VersionOf(response));
    }

    [Fact]
    public async Task Put_SecondMapWithoutIfMatch_Returns200AndNextVersion()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-second");
        await client.SendAsync(PutMap("site-second", null));

        // Act
        var response = await client.SendAsync(PutMap("site-second", null));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, await VersionOf(response));
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_CurrentIfMatch_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-current");
        await client.SendAsync(PutMap("site-current", null));

        // Act
        var response = await client.SendAsync(PutMap("site-current", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, await VersionOf(response));
    }

    [Theory]
    [InlineData("\"0\"")]
    [InlineData("\"5\"")]
    public async Task Put_StaleIfMatchOnExistingMap_Returns412(string ifMatch)
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var siteId = $"site-stale-{Guid.NewGuid():N}";
        await SeedSiteAsync("tenant-a", siteId);
        await client.SendAsync(PutMap(siteId, null));

        // Act
        var response = await client.SendAsync(PutMap(siteId, ifMatch));

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Theory]
    [InlineData("abc", false)]
    [InlineData("*", false)]
    [InlineData("abc", true)]
    public async Task Put_UnreadableIfMatch_Returns412(string ifMatch, bool firstUpload)
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var siteId = $"site-unreadable-{Guid.NewGuid():N}";
        await SeedSiteAsync("tenant-a", siteId);
        if (!firstUpload) await client.SendAsync(PutMap(siteId, null));

        // Act
        var response = await client.SendAsync(PutMap(siteId, ifMatch));

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidMapWithoutIfMatch_Returns422AndKeepsVersion()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-keep");
        await client.SendAsync(PutMap("site-keep", null));
        var invalid = MapFixtures.BaseMap.Replace("\"areaType\": \"perimeter\"", "\"areaType\": \"work\"");

        // Act
        var response = await client.SendAsync(PutMap("site-keep", null, invalid));

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var stored = await SiteMapStored("tenant-a", "site-keep");
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task Put_ParallelUploadsWithoutIfMatch_AllSucceedWithDistinctVersions()
    {
        // Arrange: 3 writers, the retry limit is 3 attempts.
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-parallel");

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.SendAsync(PutMap("site-parallel", null))));

        // Assert
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var versions = await Task.WhenAll(responses.Select(VersionOf));
        Assert.Equal(3, versions.Distinct().Count());
        Assert.Equal(versions.Max(), await SiteMapStored("tenant-a", "site-parallel"));
    }

    // Features by id, so the comparison ignores order; geometry and properties compared as JSON text.
    private static Dictionary<string, (string Geometry, string Properties)> FeaturesById(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        return doc.RootElement.GetProperty("features").EnumerateArray().ToDictionary(
            f => f.GetProperty("properties").GetProperty("id").GetString()!,
            f => (Canonical(f.GetProperty("geometry")), Canonical(f.GetProperty("properties"))));
    }

    private static string Canonical(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"\"{p.Name}\":{Canonical(p.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", e.EnumerateArray().Select(Canonical)) + "]",
        JsonValueKind.Number => e.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => e.GetRawText(),
    };

    private async Task<HttpClient> ClientWithMapAsync(string siteId, string map, string role = "Admin")
    {
        var admin = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", siteId);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(PutMap(siteId, null, map))).StatusCode);
        return role == "Admin" ? admin : _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", [role]));
    }

    [Fact]
    public async Task Get_AfterPut_ReturnsSameElements()
    {
        // Arrange
        var client = await ClientWithMapAsync("site-get", MapFixtures.BaseMap);

        // Act
        var response = await client.GetAsync(MapUrl("site-get"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GeoJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FeaturesById(MapFixtures.BaseMap), FeaturesById(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Get_RealMap_RoundTrip()
    {
        // Arrange
        var client = await ClientWithMapAsync("site-get-real", MapFixtures.RealMap);

        // Act
        var response = await client.GetAsync(MapUrl("site-get-real"));

        // Assert: holes kept, areas without capacity stay without it.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(FeaturesById(MapFixtures.RealMap), FeaturesById(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Get_ReturnsEtagWithMapVersion()
    {
        // Arrange
        var client = await ClientWithMapAsync("site-get-etag", MapFixtures.BaseMap);
        await client.SendAsync(PutMap("site-get-etag", null));

        // Act
        var response = await client.GetAsync(MapUrl("site-get-etag"));

        // Assert
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Get_NoMap_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-a", "site-get-nomap");

        // Act
        var response = await client.GetAsync(MapUrl("site-get-nomap"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownSite_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));

        // Act
        var response = await client.GetAsync(MapUrl("site-unknown"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OtherTenantSite_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SeedSiteAsync("tenant-b", "site-get-iso");
        await SiteMap.ImportAsync("tenant-b", "site-get-iso", MapFixtures.BaseMap, null, 0.5, CancellationToken.None);

        // Act
        var response = await client.GetAsync(MapUrl("site-get-iso"));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    public async Task Get_AsSafetyOrViewer_Returns200(string role)
    {
        // Arrange
        var client = await ClientWithMapAsync($"site-get-{role}", MapFixtures.BaseMap, role);

        // Act
        var response = await client.GetAsync(MapUrl($"site-get-{role}"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        // Act
        var response = await _factory.CreateClient().GetAsync(MapUrl("site-get-401"));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<long> SiteMapStored(string tenantId, string siteId) =>
        (await SiteMap.GetAsync(tenantId, siteId, CancellationToken.None))!.Version;
}
