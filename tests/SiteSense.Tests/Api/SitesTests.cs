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
    // The real site from SS-18; each test swaps in its own siteId.
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

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
    public async Task Create_ParallelSameSiteId_OneCreatedRestConflict()
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

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    public async Task Create_WithoutSiteId_Returns201WithServerId(string siteIdValue)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var body = ValidSiteNode(NewId());
        if (siteIdValue == "missing") body.AsObject().Remove("siteId");
        else body["siteId"] = null;

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(body.ToJsonString()));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var newId = created.RootElement.GetProperty("siteId").GetString()!;
        Assert.Matches("^[0-9a-f]{32}$", newId);
        Assert.Equal($"/api/v1/sites/{newId}", response.Headers.Location!.AbsolutePath);
        var get = await client.GetAsync($"/api/v1/sites/{newId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Update_CurrentIfMatch_Returns200AndUpdates()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var updated = ValidSiteNode(id);
        updated["name"] = "Renamed site";
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(updated.ToJsonString()) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("siteId").GetString());
        Assert.Equal("\"2\"", response.Headers.ETag!.Tag);
        var get = await client.GetAsync($"/api/v1/sites/{id}");
        using var site = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal("Renamed site", site.RootElement.GetProperty("name").GetString());
        Assert.Equal("\"2\"", get.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Update_WithoutBodySiteId_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var updated = ValidSiteNode(id);
        updated.AsObject().Remove("siteId");
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(updated.ToJsonString()) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("siteId").GetString());
    }

    [Fact]
    public async Task Update_MissingIfMatch_Returns412Problem()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var updated = ValidSiteNode(id);
        updated["name"] = "Renamed site";
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(updated.ToJsonString()) };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var get = await client.GetAsync($"/api/v1/sites/{id}");
        using var site = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal("Cittadella Universitaria", site.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Update_StaleIfMatch_Returns412Problem()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(ValidSite(id)) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"7\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_OtherTenantOrMissingSite_Returns404(bool existsInTenantB)
    {
        // Arrange
        var tenantA = _factory.CreateClient();
        tenantA.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var tenantB = _factory.CreateClient();
        tenantB.WithToken(TestTokens.Create("tenant-b", ["Admin"]));
        var id = NewId();
        if (existsInTenantB) await tenantB.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(ValidSite(id)) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        // Act
        var response = await tenantA.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_BodySiteIdDiffersFromUrl_Returns400Problem()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        await client.PostAsync("/api/v1/sites", Json(ValidSite(id)));
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(ValidSite(NewId())) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    [InlineData("gateway")]
    public async Task Update_NonAdmin_Returns403(string caller)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));
        var id = NewId();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/sites/{id}") { Content = Json(ValidSite(id)) };
        request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
        Assert.False(root.TryGetProperty("mapFile", out _));
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

    [Fact]
    public async Task List_ReturnsOnlyOwnTenantSites_SortedById()
    {
        // Arrange: shared store (a new ApiFactory here would re-point the static store for the rest of the class), so ids get a unique prefix.
        var p = NewId();
        var a = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var b = _factory.CreateClient().WithToken(TestTokens.Create("tenant-b", ["Admin"]));
        await a.PostAsync("/api/v1/sites", Json(ValidSite($"{p}-b")));
        await a.PostAsync("/api/v1/sites", Json(ValidSite($"{p}-a")));
        await b.PostAsync("/api/v1/sites", Json(ValidSite($"{p}-other")));

        // Act
        var response = await a.GetAsync("/api/v1/sites");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = body.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("siteId").GetString()!).ToList();
        Assert.Equal([$"{p}-a", $"{p}-b"], ids.Where(id => id.StartsWith(p, StringComparison.Ordinal)));
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }

    [Theory]
    [InlineData("Viewer")]
    [InlineData("Safety")]
    public async Task List_AsViewerOrSafety_Returns403(string role)
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", [role]));

        // Act
        var response = await client.GetAsync("/api/v1/sites");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutMapFile_Returns201()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var site = ValidSiteNode(NewId());
        site.AsObject().Remove("mapFile");

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(site.ToJsonString()));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithMapFile_IsAcceptedAndIgnored()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var site = ValidSiteNode(NewId());
        site["mapFile"] = "map.geojson";

        // Act
        var response = await client.PostAsync("/api/v1/sites", Json(site.ToJsonString()));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("mapFile", out _));
    }

    [Fact]
    public async Task Get_StoredSiteWithMapFile_StillLoads()
    {
        // Arrange: a site document written by an older version still has "mapFile".
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var id = NewId();
        var folder = Path.Combine(_factory.StoreRoot, "tenant-a", "Site");
        Directory.CreateDirectory(folder);
        var stored = ValidSiteNode(id);
        stored["id"] = id;
        stored["tenantId"] = "tenant-a";
        stored["version"] = 1;
        stored["mapFile"] = "map.geojson";
        stored.AsObject().Remove("siteId");
        await File.WriteAllTextAsync(Path.Combine(folder, id + ".json"), stored.ToJsonString());

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
