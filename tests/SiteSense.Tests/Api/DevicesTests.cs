using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class DevicesTests : IClassFixture<ApiFactory>
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

    public DevicesTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_Admin_Returns201WithBodyLocationAndETag()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal(siteId, body.RootElement.GetProperty("siteId").GetString());
        Assert.Equal($"/api/v1/sites/{siteId}/devices/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Register_StoresOnlyIdTenantVersionAndSite()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();

        // Act
        await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Assert
        var file = Path.Combine(_factory.StoreRoot, "tenant-a", "Device", id + ".json");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file));
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "id", "siteId", "tenantId", "version" }, keys);
    }

    [Fact]
    public async Task Register_SameDeviceSameSite_Returns409Problem()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Assert
        await AssertConflictAsync(response);
    }

    [Fact]
    public async Task Register_SameDeviceOtherSiteSameTenant_Returns409Problem()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteA = await CreateSiteAsync(client);
        var siteB = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{siteB}/devices", Json(Device(id)));

        // Assert
        await AssertConflictAsync(response);
    }

    [Fact]
    public async Task Register_ParallelSameDeviceTwoSites_OneCreatedRestConflict()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var sites = new[] { await CreateSiteAsync(client), await CreateSiteAsync(client) };
        var id = NewId();

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => client.PostAsync($"/api/v1/sites/{sites[i % 2]}/devices", Json(Device(id)))));

        // Assert
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = responses.Single(r => r.StatusCode == HttpStatusCode.Created).Headers.Location!.AbsolutePath;
        var get = await client.GetAsync(winner);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Register_SameDeviceOtherTenant_Returns201()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteA = await CreateSiteAsync(admin);
        var siteB = await CreateSiteAsync(otherAdmin);
        var id = NewId();
        await admin.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await otherAdmin.PostAsync($"/api/v1/sites/{siteB}/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Register_IdsDifferingOnlyInCase_BothCreated()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();

        // Act
        var lower = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));
        var upper = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id.ToUpperInvariant())));

        // Assert
        Assert.Equal(HttpStatusCode.Created, lower.StatusCode);
        Assert.Equal(HttpStatusCode.Created, upper.StatusCode);
    }

    [Fact]
    public async Task Register_UnknownSite_Returns404()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var id = NewId();

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{NewSiteId()}/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        await AssertNothingStoredAsync(client, id);
    }

    [Fact]
    public async Task Register_OtherTenantSite_Returns404()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteId = await CreateSiteAsync(admin);
        var id = NewId();

        // Act
        var response = await otherAdmin.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingStoredAsync(otherAdmin, id, siteId);
        await AssertNothingStoredAsync(admin, id, siteId);
    }

    [Fact]
    public async Task Register_InvalidSiteIdInUrl_Returns404()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var id = NewId();

        // Act
        var response = await client.PostAsync("/api/v1/sites/a.b/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNothingStoredAsync(client, id, "a.b");
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    [InlineData("gateway")]
    public async Task Register_NonAdmin_Returns403(string caller)
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));
        var id = NewId();

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertNothingStoredAsync(admin, id, siteId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("dotdot")]
    [InlineData("dot")]
    [InlineData("long")]
    public async Task Register_InvalidDeviceId_Returns400Problem(string kind)
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var (body, id) = kind switch
        {
            "missing" => ("{}", null),
            "null" => ("{\"deviceId\":null}", null),
            "empty" => (Device(""), null),
            // ".." cannot be probed by GET: HttpClient normalizes it out of the URL path.
            "dotdot" => (Device(".."), null),
            "dot" => (Device("a.b"), "a.b"),
            _ => (Device(new string('a', 129)), new string('a', 129)),
        };

        // Act
        var response = await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(body));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        if (id is not null) await AssertNothingStoredAsync(client, id, siteId);
    }

    [Fact]
    public async Task Get_OwnDevice_Returns200WithBodyAndETag()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag!.Tag);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(id, body.RootElement.GetProperty("deviceId").GetString());
        Assert.Equal(siteId, body.RootElement.GetProperty("siteId").GetString());
    }

    [Fact]
    public async Task Get_DeviceOfOtherSite_Returns404()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteA = await CreateSiteAsync(client);
        var siteB = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{siteB}/devices/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OtherTenantDevice_Returns404()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteId = await CreateSiteAsync(admin);
        var id = NewId();
        await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Act
        var response = await otherAdmin.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("a.b")]
    [InlineData("long")]
    public async Task Get_UnknownOrInvalidId_Returns404(string key)
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = key switch
        {
            "unknown" => NewId(),
            "long" => new string('a', 129),
            _ => key,
        };

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    [InlineData("gateway")]
    public async Task Get_NonAdmin_Returns403(string caller)
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        var id = NewId();
        await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Delete_CurrentIfMatch_Returns204AndRemoves()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteA = await CreateSiteAsync(client);
        var siteB = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await client.SendAsync(DeleteRequest($"/api/v1/sites/{siteA}/devices/{id}", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertNothingStoredAsync(client, id, siteA);
        var recreate = await client.PostAsync($"/api/v1/sites/{siteB}/devices", Json(Device(id)));
        Assert.Equal(HttpStatusCode.Created, recreate.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"2\"")]
    public async Task Delete_MissingOrStaleIfMatch_Returns412Problem(string? ifMatch)
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));

        // Act
        var response = await client.SendAsync(DeleteRequest($"/api/v1/sites/{siteId}/devices/{id}", ifMatch));

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var get = await client.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Delete_DeviceOfOtherSite_Returns404()
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteA = await CreateSiteAsync(client);
        var siteB = await CreateSiteAsync(client);
        var id = NewId();
        await client.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await client.SendAsync(DeleteRequest($"/api/v1/sites/{siteB}/devices/{id}", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var get = await client.GetAsync($"/api/v1/sites/{siteA}/devices/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherTenantDevice_Returns404()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteA = await CreateSiteAsync(admin);
        var siteB = await CreateSiteAsync(otherAdmin);
        var id = NewId();
        await admin.PostAsync($"/api/v1/sites/{siteA}/devices", Json(Device(id)));

        // Act
        var response = await otherAdmin.SendAsync(DeleteRequest($"/api/v1/sites/{siteB}/devices/{id}", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var get = await admin.GetAsync($"/api/v1/sites/{siteA}/devices/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("a.b")]
    [InlineData("long")]
    public async Task Delete_UnknownOrInvalidId_Returns404(string key)
    {
        // Arrange
        var client = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(client);
        var id = key switch
        {
            "unknown" => NewId(),
            "long" => new string('a', 129),
            _ => key,
        };

        // Act
        var response = await client.SendAsync(DeleteRequest($"/api/v1/sites/{siteId}/devices/{id}", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    [InlineData("gateway")]
    public async Task Delete_NonAdmin_Returns403(string caller)
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        var id = NewId();
        await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Json(Device(id)));
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.SendAsync(DeleteRequest($"/api/v1/sites/{siteId}/devices/{id}", "\"1\""));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var get = await admin.GetAsync($"/api/v1/sites/{siteId}/devices/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    private HttpClient AdminClient(string tenant)
    {
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, ["Admin"]));
        return client;
    }

    private static async Task AssertConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
    }

    // Nothing stored: an Admin of the tenant gets 404 for the device.
    private static async Task AssertNothingStoredAsync(HttpClient admin, string deviceId, string siteId = "any-site")
    {
        var get = await admin.GetAsync($"/api/v1/sites/{siteId}/devices/{deviceId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    private static async Task<string> CreateSiteAsync(HttpClient admin)
    {
        var siteId = NewSiteId();
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var response = await admin.PostAsync("/api/v1/sites", Json(site.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return siteId;
    }

    private static string NewSiteId() => $"site-{Guid.NewGuid():N}";

    // Lowercase: the store encodes uppercase letters, which would break the raw-file path.
    private static string NewId() => $"dev-{Guid.NewGuid():N}";

    private static string Device(string deviceId) => new JsonObject { ["deviceId"] = deviceId }.ToJsonString();

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static HttpRequestMessage DeleteRequest(string url, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, url);
        if (ifMatch is not null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return request;
    }

    private static string TokenFor(string caller) =>
        caller == "gateway"
            ? TestTokens.Create("tenant-a", scope: "positions:write")
            : TestTokens.Create("tenant-a", [caller]);
}
