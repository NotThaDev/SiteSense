using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using SiteSense.Tests.Api.Auth;
using SiteSense.Tests.Api.Maps;

namespace SiteSense.Tests.Api;

// SS-23: ordered processing and POST /api/v1/test/drain. Every device is new per test, so tests share one host.
// Each class owns its factories: starting a host replaces the static SiteSenseContext, so hosts must not overlap.
internal sealed record P(long TimestampMs, double Lat, double Lon, double AccuracyM);

internal static class ProcessingHelpers
{
    public const long T0 = 1793268000000;
    public const long Hour = 3_600_000;
    private const string GeoJson = "application/geo+json";
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    public static P Inside(long timestampMs) => new(timestampMs, 37.52, 15.07, 1);

    // The timeout turns a hung drain into a failure.
    public static HttpClient Client(ApiFactory factory, string tenant, string[]? roles = null, string? scope = null)
    {
        var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.WithToken(TestTokens.Create(tenant, roles, scope: scope));
        return client;
    }

    public static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    public static async Task<string> SetupDeviceAsync(ApiFactory factory, string tenant)
    {
        var admin = Client(factory, tenant, ["Admin"]);
        var siteId = $"site-{Guid.NewGuid():N}";
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsync("/api/v1/sites", Json(site.ToJsonString()))).StatusCode);
        var map = await admin.PutAsync($"/api/v1/sites/{siteId}/map", new StringContent(MapFixtures.BaseMap, Encoding.UTF8, GeoJson));
        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        var deviceId = $"dev-{Guid.NewGuid():N}";
        var device = await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Json(new JsonObject { ["deviceId"] = deviceId }.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, device.StatusCode);
        return deviceId;
    }

    public static async Task SendAsync(ApiFactory factory, string tenant, string deviceId, params long[] timestamps)
    {
        var body = JsonSerializer.Serialize(new { deviceId, positions = timestamps.Select(Inside) }, JsonSerializerOptions.Web);
        var response = await Client(factory, tenant, scope: "positions:write").PostAsync("/api/v1/positions", Json(body));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    public static async Task<HttpResponseMessage> DrainAsync(ApiFactory factory, string tenant) =>
        await Client(factory, tenant, ["Admin"]).PostAsync("/api/v1/test/drain", null);

    public static async Task DrainOkAsync(ApiFactory factory, string tenant) =>
        Assert.Equal(HttpStatusCode.NoContent, (await DrainAsync(factory, tenant)).StatusCode);

    // Read from the factory's own root, not SiteSenseContext, which belongs to whichever host started last.
    public static Task<IReadOnlyList<TrackPoint>> TrackAsync(string root, string tenant, string deviceId) =>
        new FileAppendLog(root).ReadRangeAsync<TrackPoint>(tenant, deviceId, 0, long.MaxValue, CancellationToken.None)
            .ContinueWith(t => (IReadOnlyList<TrackPoint>)t.Result.GroupBy(p => p.TimestampMs)
                .Select(g => g.OrderByDescending(p => p.Revision).First()).OrderBy(p => p.TimestampMs).ToList());
}

[Collection("Api")]
public sealed class ProcessingTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public ProcessingTests(TestModeApiFactory factory) => _factory = factory;

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    [Fact]
    public async Task Drain_OutOfOrderBatch_TrackInTimestampOrder()
    {
        // Arrange
        var device = await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-a");
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, T(3), T(1), T(5), T(2), T(4));

        // Act
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Assert
        var track = await ProcessingHelpers.TrackAsync(_factory.StoreRoot, "tenant-a", device);
        Assert.Equal([T(1), T(2), T(3), T(4), T(5)], track.Select(p => p.TimestampMs));
        Assert.All(track, p => Assert.Equal(0, p.Revision));
    }

    [Fact]
    public async Task Drain_PositionsHoursLate_ReplayedInOrder()
    {
        // Arrange
        var device = await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-a");
        var late = ProcessingHelpers.T0 - 3 * ProcessingHelpers.Hour;
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, T(0), T(1), T(2), T(3), T(4), T(5));
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Act
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, late, late + 60_000, late + 120_000);
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Assert
        var track = await ProcessingHelpers.TrackAsync(_factory.StoreRoot, "tenant-a", device);
        Assert.Equal([late, late + 60_000, late + 120_000, T(0), T(1), T(2), T(3), T(4), T(5)], track.Select(p => p.TimestampMs));
        Assert.All(track, p => Assert.Equal(1, p.Revision));
    }

    [Fact]
    public async Task Drain_SameBatchTwice_EachTimestampOnce()
    {
        // Arrange
        var device = await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-a");
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, T(1), T(2), T(3));
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Act
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, T(1), T(2), T(3));
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Assert
        var track = await ProcessingHelpers.TrackAsync(_factory.StoreRoot, "tenant-a", device);
        Assert.Equal([T(1), T(2), T(3)], track.Select(p => p.TimestampMs));
    }

    [Fact]
    public async Task Drain_OtherTenantOnly_WaitsForOwnTenant()
    {
        // Arrange
        var deviceA = await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-a");
        await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-b");
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", deviceA, T(1), T(2), T(3));

        // Act
        var other = await ProcessingHelpers.DrainAsync(_factory, "tenant-b");
        var own = await ProcessingHelpers.DrainAsync(_factory, "tenant-a");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Equal(3, (await ProcessingHelpers.TrackAsync(_factory.StoreRoot, "tenant-a", deviceA)).Count);
    }

    [Fact]
    public async Task Drain_FutureTimestamp_Processed()
    {
        // Arrange
        var device = await ProcessingHelpers.SetupDeviceAsync(_factory, "tenant-a");
        var clock = await ProcessingHelpers.Client(_factory, "tenant-a", ["Admin"])
            .PutAsync("/api/v1/test/clock", ProcessingHelpers.Json($"{{\"nowMs\":{ProcessingHelpers.T0}}}"));
        Assert.Equal(HttpStatusCode.NoContent, clock.StatusCode);
        var future = ProcessingHelpers.T0 + ProcessingHelpers.Hour;
        await ProcessingHelpers.SendAsync(_factory, "tenant-a", device, future);

        // Act
        await ProcessingHelpers.DrainOkAsync(_factory, "tenant-a");

        // Assert
        var track = await ProcessingHelpers.TrackAsync(_factory.StoreRoot, "tenant-a", device);
        Assert.Equal([future], track.Select(p => p.TimestampMs));
    }

    [Fact]
    public async Task Drain_NonAdmin_Returns403()
    {
        // Arrange
        var client = ProcessingHelpers.Client(_factory, "tenant-a", ["Worker"]);

        // Act
        var response = await client.PostAsync("/api/v1/test/drain", null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static long T(int minutes) => ProcessingHelpers.T0 + minutes * 60_000L;
}

[Collection("Api")]
public sealed class ProcessingOffTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ProcessingOffTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Drain_TestModeOff_Returns404()
    {
        // Arrange
        var client = ProcessingHelpers.Client(_factory, "tenant-a", ["Admin"]);

        // Act
        var response = await client.PostAsync("/api/v1/test/drain", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

[Collection("Api")]
public sealed class ProcessingRestartTests
{
    [Fact]
    public async Task Restart_DirtyMarker_ProcessedBeforeFirstDrain()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
        var originalTime = SiteSenseContext.Time;
        try
        {
            string device;
            using (var first = new TestModeApiFactory(root, keepStore: true))
                device = await ProcessingHelpers.SetupDeviceAsync(first, "tenant-a");

            // Crash simulation: raw positions and a dirty marker on disk, no enqueue.
            var t0 = ProcessingHelpers.T0;
            var raw = new[] { 3, 1, 2 }.Select(m => new Position(device, t0 + m * 60_000L, 37.52, 15.07, 1)).ToList();
            await new FileAppendLog(root).AppendAsync("tenant-a", device, raw, CancellationToken.None);
            await new FileDocumentStore(root).SaveAsync("tenant-a",
                new DeviceProcessing { Id = device, TenantId = "tenant-a", DirtyFromMs = t0 + 60_000 }, CancellationToken.None);

            // Act
            using var second = new TestModeApiFactory(root, keepStore: true);
            var response = await ProcessingHelpers.DrainAsync(second, "tenant-a");

            // Assert
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var track = await ProcessingHelpers.TrackAsync(root, "tenant-a", device);
            Assert.Equal([t0 + 60_000, t0 + 120_000, t0 + 180_000], track.Select(p => p.TimestampMs));
        }
        finally
        {
            SiteSenseContext.Time = originalTime;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
