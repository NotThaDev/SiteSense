using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class TrackTests : IClassFixture<ApiFactory>
{
    // UTC hour aligned, so the first hour file starts exactly at T0.
    private const long T0 = 1793268000000;
    private const long Hour = 3600000;

    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    private readonly ApiFactory _factory;

    public TrackTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Track_returns_points_in_range_sorted_with_area()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        await SeedAsync("tenant-a", deviceId, Pt(deviceId, T0 + 2000, "work-1"), Pt(deviceId, T0 + 1000, null), Pt(deviceId, T0 + 3000, "rest-1"));

        var response = await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0}&toMs={T0 + 10000}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal(new long[] { T0 + 1000, T0 + 2000, T0 + 3000 }, Stamps(body));
        var points = body["points"]!.AsArray();
        Assert.Null(points[0]!["areaId"]);
        Assert.Equal("work-1", (string?)points[1]!["areaId"]);
        Assert.Equal(45.5, (double)points[0]!["lat"]!);
        Assert.Equal(9.5, (double)points[0]!["lon"]!);
    }

    [Fact]
    public async Task Track_range_is_half_open()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        await SeedAsync("tenant-a", deviceId, Pt(deviceId, T0 + 1000), Pt(deviceId, T0 + 2000), Pt(deviceId, T0 + 3000));

        var body = await ReadAsync(await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0 + 1000}&toMs={T0 + 3000}"));

        Assert.Equal(new long[] { T0 + 1000, T0 + 2000 }, Stamps(body));
    }

    [Fact]
    public async Task Track_range_crossing_an_hour_file_boundary()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        await SeedAsync("tenant-a", deviceId, Pt(deviceId, T0 + Hour - 1000), Pt(deviceId, T0 + Hour), Pt(deviceId, T0 + Hour + 1000));

        var body = await ReadAsync(await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0}&toMs={T0 + 2 * Hour}"));

        Assert.Equal(new long[] { T0 + Hour - 1000, T0 + Hour, T0 + Hour + 1000 }, Stamps(body));
    }

    [Fact]
    public async Task Track_pages_with_limit_and_cursor_without_gaps_or_repeats()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        var all = Enumerable.Range(1, 5).Select(i => T0 + i * 1000L).ToArray();
        await SeedAsync("tenant-a", deviceId, all.Select(t => Pt(deviceId, t)).ToArray());
        var client = Client("tenant-a", "Safety");

        var seen = new List<long>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = $"fromMs={T0}&toMs={T0 + 10000}&limit=2" + (cursor is null ? "" : $"&cursor={cursor}");
            var body = await ReadAsync(await GetAsync(client, deviceId, query));
            seen.AddRange(Stamps(body));
            cursor = (string?)body["nextCursor"];
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(all, seen);
    }

    [Fact]
    public async Task Track_last_page_has_no_nextCursor()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        await SeedAsync("tenant-a", deviceId, Pt(deviceId, T0 + 1000), Pt(deviceId, T0 + 2000));

        var body = await ReadAsync(await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0}&toMs={T0 + 10000}&limit=2"));

        Assert.False(body.ContainsKey("nextCursor"));
    }

    [Fact]
    public async Task Track_safety_gets_200()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");

        var response = await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await ReadAsync(response))["points"]!.AsArray());
    }

    [Fact]
    public async Task Track_admin_gets_403()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");

        var response = await GetAsync(Client("tenant-a", "Admin"), deviceId, $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Track_viewer_gets_403()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");

        var response = await GetAsync(Client("tenant-a", "Viewer"), deviceId, $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Track_unauthenticated_gets_401()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");

        var response = await GetAsync(_factory.CreateClient(), deviceId, $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Track_other_tenant_device_gets_404_same_as_unknown()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        var client = Client("tenant-b", "Safety");

        var other = await GetAsync(client, deviceId, $"fromMs={T0}&toMs={T0 + 1000}");
        var unknown = await GetAsync(client, NewId(), $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal((string?)(await ReadAsync(unknown))["title"], (string?)(await ReadAsync(other))["title"]);
    }

    [Fact]
    public async Task Track_invalid_device_id_gets_404()
    {
        var response = await GetAsync(Client("tenant-a", "Safety"), "..%2Fx", $"fromMs={T0}&toMs={T0 + 1000}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("fromMs=-1&toMs=1000")]
    [InlineData("fromMs=1000&toMs=-1")]
    [InlineData("fromMs=2000&toMs=1000")]
    [InlineData("fromMs=1000&toMs=1000")]
    [InlineData("toMs=1000")]
    [InlineData("fromMs=1000")]
    [InlineData("fromMs=1000&toMs=2000&limit=0")]
    [InlineData("fromMs=1000&toMs=2000&limit=5001")]
    [InlineData("fromMs=1000&toMs=2000&cursor=abc")]
    [InlineData("fromMs=1000&toMs=2000&cursor=999")]
    [InlineData("fromMs=1000&toMs=2000&cursor=2000")]
    public async Task Track_bad_input_gets_400(string query)
    {
        var deviceId = await SetupDeviceAsync("tenant-a");

        var response = await GetAsync(Client("tenant-a", "Safety"), deviceId, query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Track_hides_dropped_points()
    {
        var deviceId = await SetupDeviceAsync("tenant-a");
        await SeedAsync("tenant-a", deviceId, Pt(deviceId, T0 + 1000), Pt(deviceId, T0 + 2000) with { Dropped = true }, Pt(deviceId, T0 + 3000));

        var body = await ReadAsync(await GetAsync(Client("tenant-a", "Safety"), deviceId, $"fromMs={T0}&toMs={T0 + 10000}"));

        Assert.Equal(new long[] { T0 + 1000, T0 + 3000 }, Stamps(body));
    }

    private static TrackPoint Pt(string deviceId, long ts, string? areaId = null) => new(deviceId, ts, 45.5, 9.5, 1, 1, false, areaId);

    private async Task SeedAsync(string tenant, string deviceId, params TrackPoint[] points) =>
        await new FileAppendLog(_factory.StoreRoot).AppendAsync(tenant, deviceId, points, CancellationToken.None);

    private HttpClient Client(string tenant, string role) =>
        _factory.CreateClient().WithToken(TestTokens.Create(tenant, [role]));

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string deviceId, string query) =>
        client.GetAsync($"/api/v1/devices/{deviceId}/track?{query}");

    private static async Task<JsonObject> ReadAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    private static long[] Stamps(JsonObject body) =>
        body["points"]!.AsArray().Select(p => (long)p!["timestampMs"]!).ToArray();

    // Registers a new device on a new site (no map needed: the track is seeded straight into the log).
    private async Task<string> SetupDeviceAsync(string tenant)
    {
        var admin = _factory.CreateClient().WithToken(TestTokens.Create(tenant, ["Admin"]));
        var siteId = $"site-{Guid.NewGuid():N}";
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var created = await admin.PostAsync("/api/v1/sites", Content(site.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var deviceId = NewId();
        var device = await admin.PostAsync($"/api/v1/sites/{siteId}/devices", Content(new JsonObject { ["deviceId"] = deviceId }.ToJsonString()));
        Assert.Equal(HttpStatusCode.Created, device.StatusCode);
        return deviceId;
    }

    private static StringContent Content(string body) => new(body, Encoding.UTF8, "application/json");

    // Lowercase: the store encodes uppercase letters, which would break the raw-file path.
    private static string NewId() => $"dev-{Guid.NewGuid():N}";
}
