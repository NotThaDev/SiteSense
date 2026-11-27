using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Tests.Api.Auth;
using SiteSense.Tests.Api.Maps;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class RoutesTests : IClassFixture<ApiFactory>
{
    private const string Tenant = "tenant-a";
    // A (lon 15.0700) to B (lon 15.0720) at lat 37.5200, about 177 m apart; the restricted box sits on the A-B stretch.
    private const string Short = "[[15.0700, 37.5200], [15.0720, 37.5200]]";
    private const string Around = "[[15.0700, 37.5200], [15.0700, 37.5210], [15.0720, 37.5210], [15.0720, 37.5200]]";

    private readonly ApiFactory _factory;

    public RoutesTests(ApiFactory factory)
    {
        _factory = factory;
        _factory.CreateClient(); // creating the host resets the static store, so do it before any seeding
    }

    private HttpClient Client(string tenant = Tenant, string role = "Admin") =>
        _factory.CreateClient().WithToken(TestTokens.Create(tenant, [role]));

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string SiteJson(double speed) =>
        string.Create(CultureInfo.InvariantCulture, $$$"""{"name":"Test site","timeZone":"Europe/Rome","navigation":{"walkingSpeedMps":{{{speed}}},"unpavedSpeedFactor":0.8,"stepsSpeedFactor":0.5,"connectToleranceM":0.5,"maxSnapDistanceM":30}}""");

    // Valid map: the paths, an assembly point on the first and an entrance on the last, an optional restricted box on the A-B stretch.
    private static string Map(string[] paths, bool restricted = false)
    {
        var features = new List<string>
        {
            """{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[15.06,37.51],[15.08,37.51],[15.08,37.53],[15.06,37.53],[15.06,37.51]]]},"properties":{"id":"perimeter","kind":"area","name":"Site","areaType":"perimeter"}}""",
            """{"type":"Feature","geometry":{"type":"Point","coordinates":[15.0700,37.5200]},"properties":{"id":"ap-1","kind":"assemblyPoint","name":"A","capacity":10}}""",
            """{"type":"Feature","geometry":{"type":"Point","coordinates":[15.0700,37.5200]},"properties":{"id":"e-1","kind":"entrance","name":"Gate"}}""",
        };
        if (restricted)
            features.Add("""{"type":"Feature","geometry":{"type":"Polygon","coordinates":[[[15.0709,37.5199],[15.0711,37.5199],[15.0711,37.5201],[15.0709,37.5201],[15.0709,37.5199]]]},"properties":{"id":"r-1","kind":"area","name":"Pit","areaType":"restricted"}}""");
        for (var i = 0; i < paths.Length; i++)
            features.Add($$$"""{"type":"Feature","geometry":{"type":"LineString","coordinates":{{{paths[i]}}}},"properties":{"id":"p{{{i}}}","kind":"path","surface":"paved","steps":false,"slopePercent":0,"widthM":2}}""");
        return $$$"""{"type":"FeatureCollection","features":[{{{string.Join(",", features)}}}]}""";
    }

    private async Task<string> SeedAsync(string map, double speed = 1.3, double toleranceM = 0.5, string tenant = Tenant)
    {
        var siteId = $"site-{Guid.NewGuid():N}";
        await SiteSenseContext.Store.SaveAsync(tenant, new Site
        {
            Id = siteId, TenantId = tenant, Name = "Test site", TimeZone = "Europe/Rome",
            Navigation = new NavigationSettings(speed, 0.8, 0.5, toleranceM, 30),
        }, CancellationToken.None);
        await PutMapAsync(siteId, map, tenant);
        return siteId;
    }

    private async Task PutMapAsync(string siteId, string map, string tenant = Tenant)
    {
        var response = await Client(tenant).PutAsync($"/api/v1/sites/{siteId}/map", new StringContent(map, Encoding.UTF8, "application/geo+json"));
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string Body(double fromLat, double fromLon, double toLat, double toLon) =>
        string.Create(CultureInfo.InvariantCulture, $$$"""{"from":{"lat":{{{fromLat}}},"lon":{{{fromLon}}}},"to":{"lat":{{{toLat}}},"lon":{{{toLon}}}}}""");

    // A to B on the fixture maps.
    private static readonly string AToB = Body(37.52, 15.07, 37.52, 15.072);

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string siteId, string body) =>
        client.PostAsync($"/api/v1/sites/{siteId}/routes", Json(body));

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Post_RealMap_Returns200LineString()
    {
        // Arrange
        var siteId = await SeedAsync(MapFixtures.RealMap, toleranceM: MapFixtures.RealMapToleranceM);
        using var map = JsonDocument.Parse(MapFixtures.RealMap);
        var path = map.RootElement.GetProperty("features").EnumerateArray()
            .First(f => f.GetProperty("properties").GetProperty("kind").GetString() == "path")
            .GetProperty("geometry").GetProperty("coordinates");
        var a = path[0];
        var b = path[1];

        // Act
        var response = await PostAsync(Client(), siteId, Body(a[1].GetDouble(), a[0].GetDouble(), b[1].GetDouble(), b[0].GetDouble()));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadAsync(response);
        var geometry = body.RootElement.GetProperty("geometry");
        Assert.Equal("LineString", geometry.GetProperty("type").GetString());
        Assert.True(geometry.GetProperty("coordinates").GetArrayLength() >= 2);
        Assert.True(body.RootElement.GetProperty("distanceM").GetDouble() > 0);
        Assert.True(body.RootElement.GetProperty("durationS").GetDouble() > 0);
    }

    [Fact]
    public async Task Post_AvoidsRestrictedArea()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short, Around], restricted: true));

        // Act
        var response = await PostAsync(Client(), siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadAsync(response);
        var distance = body.RootElement.GetProperty("distanceM").GetDouble();
        Assert.InRange(distance, 390, 410); // the detour is about 399 m; the straight stretch is 177 m
        Assert.InRange(body.RootElement.GetProperty("durationS").GetDouble(), distance / 1.3 - 1, distance / 1.3 + 1);
        foreach (var c in body.RootElement.GetProperty("geometry").GetProperty("coordinates").EnumerateArray())
        {
            var inside = c[0].GetDouble() is > 15.0709 and < 15.0711 && c[1].GetDouble() is > 37.5199 and < 37.5201;
            Assert.False(inside);
        }
    }

    [Fact]
    public async Task Post_StartTooFar_Returns422()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(Client(), siteId, Body(37.525, 15.075, 37.52, 15.072));

        // Assert
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = await ReadAsync(response);
        Assert.Equal("START_TOO_FAR", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Post_Unreachable_Returns422()
    {
        // Arrange: two paths with no connection between them
        var siteId = await SeedAsync(Map([Short, "[[15.0700, 37.5220], [15.0720, 37.5220]]"]));

        // Act
        var response = await PostAsync(Client(), siteId, Body(37.52, 15.07, 37.522, 15.072));

        // Assert
        Assert.Equal((HttpStatusCode)422, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("UNREACHABLE", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Post_BadLat_Returns400()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(Client(), siteId, Body(91, 15.07, 37.52, 15.072));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_MissingTo_Returns400()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(Client(), siteId, """{"from":{"lat":37.52,"lon":15.07}}""");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_NoMap_Returns404()
    {
        // Arrange
        var siteId = $"site-{Guid.NewGuid():N}";
        await SiteSenseContext.Store.SaveAsync(Tenant, new Site
        {
            Id = siteId, TenantId = Tenant, Name = "No map", TimeZone = "Europe/Rome",
            Navigation = new NavigationSettings(1.3, 0.8, 0.5, 0.5, 30),
        }, CancellationToken.None);

        // Act
        var response = await PostAsync(Client(), siteId, AToB);

        // Assert
        Assert.True(HttpStatusCode.NotFound == response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Post_OtherTenant_Returns404()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(Client("tenant-b"), siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    public async Task Post_AllRoles_Allowed(string role)
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(Client(role: role), siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutToken_Returns401()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));

        // Act
        var response = await PostAsync(_factory.CreateClient(), siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_GatewayToken_Returns403()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));
        var gateway = _factory.CreateClient().WithToken(TestTokens.Create(Tenant, scope: "positions:write"));

        // Act
        var response = await PostAsync(gateway, siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AfterMapReplaced_UsesNewGraph()
    {
        // Arrange: the first map blocks A-B (only a restricted short path), so the first call is warm in the cache
        var siteId = await SeedAsync(Map([Short], restricted: true));
        var client = Client();
        var first = await PostAsync(client, siteId, AToB);
        Assert.Equal((HttpStatusCode)422, first.StatusCode);

        // Act
        await PutMapAsync(siteId, Map([Short, Around], restricted: true));
        var second = await PostAsync(client, siteId, AToB);

        // Assert
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var body = await ReadAsync(second);
        Assert.InRange(body.RootElement.GetProperty("distanceM").GetDouble(), 390, 410);
    }

    [Fact]
    public async Task Post_AfterSiteSpeedChanged_UsesNewSpeed()
    {
        // Arrange
        var siteId = await SeedAsync(Map([Short]));
        var client = Client();
        using var before = await ReadAsync(await PostAsync(client, siteId, AToB));

        // Act
        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{siteId}") { Content = Json(SiteJson(2.6)) };
        put.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(put)).StatusCode);
        using var after = await ReadAsync(await PostAsync(client, siteId, AToB));

        // Assert
        var durationBefore = before.RootElement.GetProperty("durationS").GetDouble();
        var durationAfter = after.RootElement.GetProperty("durationS").GetDouble();
        Assert.InRange(durationAfter, durationBefore / 2 - 0.2, durationBefore / 2 + 0.2);
    }
}
