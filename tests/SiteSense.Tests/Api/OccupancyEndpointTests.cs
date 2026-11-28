using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class OccupancyEndpointTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268000000;
    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public OccupancyEndpointTests(TestModeApiFactory factory)
    {
        _factory = factory;
        // Build the host first: its startup sets the static clock.
        _ = _factory.Services;
        SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(NowMs));
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    private HttpClient Client(string tenant, string role) => _factory.CreateClient().WithToken(TestTokens.Create(tenant, [role]));

    // Creating the host resets the static store, so seed after CreateClient.
    private static async Task SeedAsync(string tenant, string siteId)
    {
        await SiteSenseContext.Store.SaveAsync(tenant, new Site
        {
            Id = siteId, TenantId = tenant, Name = "Test site", TimeZone = "Europe/Rome",
            Navigation = new NavigationSettings(1.3, 0.8, 0.5, 0.5, 30),
        }, CancellationToken.None);
        var ring = new[] { new[] { new[] { 9.1, 45.4 }, new[] { 9.3, 45.4 }, new[] { 9.3, 45.6 }, new[] { 9.1, 45.6 } } };
        await SiteSenseContext.Store.SaveAsync(tenant, new SiteMap { Id = siteId, TenantId = tenant, Areas = [new("perimeter", "Site", "perimeter", null, ring)] }, CancellationToken.None);
        await Device.CreateAsync(tenant, new Device { Id = $"d-{siteId}", TenantId = tenant, SiteId = siteId }, CancellationToken.None);
        await TrackPoint.AppendAsync(tenant, $"d-{siteId}", [new TrackPoint($"d-{siteId}", NowMs - 60_000, 45.5, 9.2, 4.5, 0, false, "perimeter")], CancellationToken.None);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    public async Task Get_AllowedRole_Returns200WithCounts(string role)
    {
        var client = Client("tenant-a", role);
        await SeedAsync("tenant-a", $"site-occ-{role}");

        var response = await client.GetAsync($"/api/v1/sites/site-occ-{role}/occupancy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var areas = doc.RootElement.GetProperty("areas");
        Assert.Equal("perimeter", areas[0].GetProperty("areaId").GetString());
        Assert.Equal(1, areas[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Get_WithoutToken_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/sites/site-occ/occupancy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OtherTenantsSite_Returns404()
    {
        var client = Client("tenant-b", "Admin");
        await SeedAsync("tenant-a", "site-occ-a");

        var response = await client.GetAsync("/api/v1/sites/site-occ-a/occupancy");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_UnknownSite_Returns404()
    {
        var response = await Client("tenant-a", "Admin").GetAsync("/api/v1/sites/nope/occupancy");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AtMsFollowsTestClock()
    {
        var client = Client("tenant-a", "Admin");
        await SeedAsync("tenant-a", "site-occ-clock");
        await client.PutAsync("/api/v1/test/clock", JsonContent.Create(new { nowMs = NowMs + 6 * 60_000 }));

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/api/v1/sites/site-occ-clock/occupancy"));

        Assert.Equal(NowMs + 6 * 60_000, doc.RootElement.GetProperty("atMs").GetInt64());
        Assert.Equal(0, doc.RootElement.GetProperty("areas")[0].GetProperty("count").GetInt32());
    }
}
