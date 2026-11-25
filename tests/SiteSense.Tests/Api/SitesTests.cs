using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class SitesTests : IClassFixture<ApiFactory>
{
    // Same content as data/site.json (SS-18); read the file once this stack's base is main.
    private const string ValidSiteJson = """
        {"siteId":"cittadella-catania","name":"Cittadella Universitaria","timeZone":"Europe/Rome","mapFile":"map.geojson","navigation":{"walkingSpeedMps":1.3,"unpavedSpeedFactor":0.8,"stepsSpeedFactor":0.5,"connectToleranceM":0.5,"maxSnapDistanceM":30}}
        """;

    private readonly ApiFactory _factory;

    public SitesTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_Admin_Returns201WithSiteIdLocationAndETag()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("siteId").GetString());
        Assert.Equal($"/api/v1/sites/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Create_DuplicateSiteId_Returns409Problem()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Create_ParallelSameSiteId_OneCreatedOneConflict()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync("/api/v1/sites", Json(ValidSite(id)))));

        // Assert
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    [InlineData("gateway")]
    public async Task Create_NonAdmin_Returns403(string caller)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(ValidSite(NewId())));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("timeZone")]
    [InlineData("walkingSpeedMps")]
    [InlineData("unpavedSpeedFactor")]
    [InlineData("stepsSpeedFactor")]
    [InlineData("siteId-invalid")]
    [InlineData("siteId-missing")]
    [InlineData("navigation-missing")]
    [InlineData("navigation-null")]
    [InlineData("name-empty")]
    public async Task Create_InvalidBody_Returns400Problem(string mutation)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var siteId = mutation == "siteId-invalid" ? "a.b" : NewId();
        var body = Mutate(ValidSiteNode(siteId), mutation);

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(body.ToJsonString()));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (mutation == "siteId-missing") return;
        var get = await client.GetAsync($"/api/v1/sites/{siteId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Create_SameSiteIdOtherTenant_Returns201()
    {
        // Arrange
        var admin = _factory.CreateClient();
        admin.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var otherAdmin = _factory.CreateClient();
        otherAdmin.WithToken(TestTokens.Create("tenant-b", ["Admin"]));
        var id = NewId();
        await admin.PostAsync("/api/v1/sites", Json(ValidSite(id)));

        // Act
        var response = await otherAdmin.PostAsync("/api/v1/sites", Json(ValidSite(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Get_OwnSite_Returns200WithBodyAndETag()
    {
        // Arrange
        var admin = _factory.CreateClient();
        admin.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await admin.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var viewer = _factory.CreateClient();
        viewer.WithToken(TestTokens.Create("tenant-a", ["Viewer"]));

        // Act
        var response = await viewer.GetAsync($"/api/v1/sites/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var nav = root.GetProperty("navigation");
        Assert.Equal(id, root.GetProperty("siteId").GetString());
        Assert.Equal("Cittadella Universitaria", root.GetProperty("name").GetString());
        Assert.Equal("Europe/Rome", root.GetProperty("timeZone").GetString());
        Assert.Equal("map.geojson", root.GetProperty("mapFile").GetString());
        Assert.Equal(1.3, nav.GetProperty("walkingSpeedMps").GetDouble());
        Assert.Equal(0.8, nav.GetProperty("unpavedSpeedFactor").GetDouble());
        Assert.Equal(0.5, nav.GetProperty("stepsSpeedFactor").GetDouble());
        Assert.Equal(0.5, nav.GetProperty("connectToleranceM").GetDouble());
        Assert.Equal(30, nav.GetProperty("maxSnapDistanceM").GetDouble());
    }

    [Fact]
    public async Task Get_OtherTenantSite_Returns404()
    {
        // Arrange
        var admin = _factory.CreateClient();
        admin.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await admin.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var otherAdmin = _factory.CreateClient();
        otherAdmin.WithToken(TestTokens.Create("tenant-b", ["Admin"]));

        // Act
        var response = await otherAdmin.GetAsync($"/api/v1/sites/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("missing-site")]
    [InlineData("..")]
    [InlineData("a.b")]
    [InlineData("long")]
    public async Task Get_UnknownOrInvalidId_Returns404(string key)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = key switch
        {
            "long" => new string('a', 129),
            _ => key,
        };

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static string NewId() => $"site-{Guid.NewGuid():N}";

    private static string ValidSite(string siteId) => ValidSiteNode(siteId).ToJsonString();

    private static JsonNode ValidSiteNode(string siteId)
    {
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        return site;
    }

    private static JsonNode Mutate(JsonNode site, string mutation)
    {
        var nav = site["navigation"]!.AsObject();
        switch (mutation)
        {
            case "timeZone": site["timeZone"] = "Mars/Olympus"; break;
            case "walkingSpeedMps": nav["walkingSpeedMps"] = 0; break;
            case "unpavedSpeedFactor": nav["unpavedSpeedFactor"] = 0; break;
            case "stepsSpeedFactor": nav["stepsSpeedFactor"] = 1.5; break;
            case "siteId-invalid": break; // the caller already set siteId to "a.b"
            case "siteId-missing": site.AsObject().Remove("siteId"); break;
            case "navigation-missing": site.AsObject().Remove("navigation"); break;
            case "navigation-null": site["navigation"] = null; break;
            case "name-empty": site["name"] = ""; break;
            default: throw new ArgumentException($"Unknown mutation '{mutation}'.", nameof(mutation));
        }
        return site;
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static string TokenFor(string caller) =>
        caller == "gateway"
            ? TestTokens.Create("tenant-a", scope: "positions:write")
            : TestTokens.Create("tenant-a", [caller]);
}
