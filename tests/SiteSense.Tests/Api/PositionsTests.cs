using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using SiteSense.Tests.Api.Auth;
using SiteSense.Tests.Api.Maps;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class PositionsTests : IClassFixture<ApiFactory>
{
    // UTC hour aligned, so the first hour file starts exactly at T0.
    private const long T0 = 1793268000000;
    private const string GeoJson = "application/geo+json";

    // The real site from SS-18; each test creates its own site.
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    private readonly ApiFactory _factory;

    public PositionsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Post_ValidBatch_Returns202AndStores()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = new[] { Inside(T0), Inside(T0 + 1000), Inside(T0 + 2000) };

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(3, result.Received);
        Assert.Equal(3, result.Stored);
        Assert.Equal(0, result.Duplicates);
        Assert.Equal(0, result.OutsidePerimeter);
        Assert.Equal(3, (await StoredAsync("tenant-a", deviceId)).Count);
    }

    [Fact]
    public async Task Post_SameBatchTwice_StoresEachPositionOnce()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = new[] { Inside(T0), Inside(T0 + 1000), Inside(T0 + 2000) };
        await PostAsync("tenant-a", deviceId, positions);

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(0, result.Stored);
        Assert.Equal(3, result.Duplicates);
        Assert.Equal(3, (await StoredAsync("tenant-a", deviceId)).Count);
    }

    [Fact]
    public async Task Post_DuplicateTimestampInBatch_StoresFirst()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = new[] { Inside(T0, accuracyM: 1), Inside(T0, accuracyM: 2) };

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(1, result.Stored);
        Assert.Equal(1, result.Duplicates);
        var stored = await StoredAsync("tenant-a", deviceId);
        Assert.Single(stored);
        Assert.Equal(1.0, stored[0].AccuracyM);
    }

    [Fact]
    public async Task Post_PositionOutsidePerimeter_NeverStored()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = new[] { Inside(T0), Outside(T0 + 1000) };

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(1, result.OutsidePerimeter);
        Assert.Equal(1, result.Stored);
        Assert.Single(await StoredAsync("tenant-a", deviceId));

        // The outside lon 15.10 must not reach any file of the tenant.
        var positionDir = Path.Combine(_factory.StoreRoot, "tenant-a", "Position");
        Assert.True(Directory.Exists(positionDir));
        foreach (var file in Directory.GetFiles(positionDir, "*.jsonl", SearchOption.AllDirectories))
            Assert.DoesNotContain("15.1", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task Post_PositionOnPerimeterBoundary_IsStored()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var boundary = new P(T0, 37.52, 15.06, 1);

        // Act
        var response = await PostAsync("tenant-a", deviceId, [boundary]);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(1, result.Stored);
        Assert.Equal(0, result.OutsidePerimeter);
        Assert.Single(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_SiteWithoutMap_StoresNothing()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a", withMap: false);

        // Act
        var response = await PostAsync("tenant-a", deviceId, [Inside(T0)]);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(0, result.Stored);
        Assert.Equal(1, result.OutsidePerimeter);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_UnknownDevice_Returns404()
    {
        // Arrange
        await SetupDeviceAsync("tenant-a");
        var unknownId = NewId();

        // Act
        var response = await PostAsync("tenant-a", unknownId, [Inside(T0)]);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", unknownId));
    }

    [Fact]
    public async Task Post_OtherTenantDevice_Returns404AndStoresNothing()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");

        // Act
        var response = await PostAsync("tenant-b", deviceId, [Inside(T0)]);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(_factory.StoreRoot, "tenant-b", "Position", deviceId)));
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_5000Positions_Returns202AndStoresAll()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = Enumerable.Range(0, 5000).Select(i => Inside(T0 + i * 1000L)).ToArray();

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await ReadResultAsync(response);
        Assert.Equal(5000, result.Stored);
        Assert.Equal(5000, (await StoredAsync("tenant-a", deviceId)).Count);
    }

    [Fact]
    public async Task Post_5001Positions_Returns400()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var positions = Enumerable.Range(0, 5001).Select(i => Inside(T0 + i * 1000L)).ToArray();

        // Act
        var response = await PostAsync("tenant-a", deviceId, positions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Theory]
    [InlineData("{\"timestampMs\":1793268000000,\"lat\":91,\"lon\":15.07,\"accuracyM\":1}", "91")]
    [InlineData("{\"timestampMs\":1793268000000,\"lat\":37.52,\"lon\":-181,\"accuracyM\":1}", null)]
    [InlineData("{\"timestampMs\":1793268000000,\"lat\":37.52,\"lon\":15.07,\"accuracyM\":-1}", null)]
    [InlineData("{\"timestampMs\":-1,\"lat\":37.52,\"lon\":15.07,\"accuracyM\":1}", null)]
    [InlineData("{\"timestampMs\":1793268000000,\"lon\":15.07,\"accuracyM\":1}", null)]
    [InlineData("{\"lat\":37.52,\"lon\":15.07,\"accuracyM\":1}", null)]
    public async Task Post_InvalidField_Returns400(string position, string? forbiddenText)
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var body = $"{{\"deviceId\":\"{deviceId}\",\"positions\":[{position}]}}";

        // Act
        var response = await PostRawAsync("tenant-a", body);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        if (forbiddenText is not null)
        {
            // Messages name the field only, never the rejected value; check the errors part, not traceId.
            using var doc = JsonDocument.Parse(text);
            Assert.DoesNotContain(forbiddenText, doc.RootElement.GetProperty("errors").GetRawText());
        }
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_MissingDeviceId_Returns400()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var body = $"{{\"positions\":[{Json(Inside(T0))}]}}";

        // Act
        var response = await PostRawAsync("tenant-a", body);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_EmptyPositions_Returns400()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");

        // Act
        var response = await PostAsync("tenant-a", deviceId, []);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_AdminUserToken_Returns403()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var client = AdminClient("tenant-a");

        // Act
        var response = await client.PostAsync("/api/v1/positions", Content(BatchJson(deviceId, [Inside(T0)])));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    [Fact]
    public async Task Post_NoToken_Returns401()
    {
        // Arrange
        var deviceId = await SetupDeviceAsync("tenant-a");
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/positions", Content(BatchJson(deviceId, [Inside(T0)])));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await StoredAsync("tenant-a", deviceId));
    }

    // Position as the gateway sends it; the record's JSON names are camelCase.
    private sealed record P(long TimestampMs, double Lat, double Lon, double AccuracyM);

    private sealed record BatchResult(int Received, int Stored, int Duplicates, int OutsidePerimeter);

    private static P Inside(long timestampMs, double accuracyM = 1) => new(timestampMs, 37.52, 15.07, accuracyM);

    private static P Outside(long timestampMs) => new(timestampMs, 37.52, 15.10, 1);

    private HttpClient AdminClient(string tenant)
    {
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, ["Admin"]));
        return client;
    }

    private HttpClient GatewayClient(string tenant)
    {
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, scope: "positions:write"));
        return client;
    }

    // Creates a site (with the base map unless withMap is false) and registers a new device in it.
    private async Task<string> SetupDeviceAsync(string tenant, bool withMap = true)
    {
        var admin = AdminClient(tenant);
        var siteId = $"site-{Guid.NewGuid():N}";
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var created = await admin.PostAsync("/api/v1/sites", Content(site.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        if (withMap)
        {
            var map = await admin.PutAsync($"/api/v1/sites/{siteId}/map", new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson));
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        }

        var deviceId = NewId();
        var device = await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Content(new JsonObject { ["deviceId"] = deviceId }.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, device.StatusCode);
        return deviceId;
    }

    private async Task<HttpResponseMessage> PostAsync(string tenant, string deviceId, IEnumerable<P> positions) =>
        await GatewayClient(tenant).PostAsync("/api/v1/positions", Content(BatchJson(deviceId, positions)));

    private async Task<HttpResponseMessage> PostRawAsync(string tenant, string body) =>
        await GatewayClient(tenant).PostAsync("/api/v1/positions", Content(body));

    private async Task<IReadOnlyList<Position>> StoredAsync(string tenant, string deviceId) =>
        await new FileAppendLog(_factory.StoreRoot).ReadRangeAsync<Position>(tenant, deviceId, 0, long.MaxValue / 2, CancellationToken.None);

    private static async Task<BatchResult> ReadResultAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<BatchResult>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web)!;

    private static string BatchJson(string deviceId, IEnumerable<P> positions) =>
        JsonSerializer.Serialize(new { deviceId, positions }, JsonSerializerOptions.Web);

    private static string Json(P position) => JsonSerializer.Serialize(position, JsonSerializerOptions.Web);

    private static StringContent Content(string body) => new(body, Encoding.UTF8, "application/json");

    // Lowercase: the store encodes uppercase letters, which would break the raw-file path.
    private static string NewId() => $"dev-{Guid.NewGuid():N}";
}
