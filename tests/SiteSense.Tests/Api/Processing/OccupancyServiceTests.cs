using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Realtime;
using SiteSense.Api.Services;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Processing;

[Collection("Api")]
public sealed class OccupancyServiceTests : IDisposable
{
    private const long Now = 1793268000000;
    private const long Min = 60_000;
    private const string Tenant = "tenant-a";
    private const string Site = "site-01";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _prevStore = SiteSenseContext.Store;
    private readonly IAppendLog _prevLog = SiteSenseContext.Log;
    private readonly OccupancyService _svc = new(new NoopPublisher(), NullLogger<OccupancyService>.Instance);

    public OccupancyServiceTests()
    {
        SiteSenseContext.Store = new FileDocumentStore(_root);
        SiteSenseContext.Log = new FileAppendLog(_root);
    }

    public void Dispose()
    {
        SiteSenseContext.Store = _prevStore;
        SiteSenseContext.Log = _prevLog;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static Task SeedMapAsync(string tenant = Tenant, string site = Site, params string[] subAreas)
    {
        var ring = new[] { new[] { new[] { 9.1, 45.4 }, new[] { 9.3, 45.4 }, new[] { 9.3, 45.6 }, new[] { 9.1, 45.6 } } };
        List<MapArea> areas = [new("perimeter", "Site", "perimeter", null, ring), .. subAreas.Select(id => new MapArea(id, id, "work", null, ring))];
        return SiteSenseContext.Store.SaveAsync(tenant, new SiteMap { Id = site, TenantId = tenant, Areas = areas }, CancellationToken.None);
    }

    private static Task AddDeviceAsync(string id, string tenant = Tenant, string site = Site) =>
        Device.CreateAsync(tenant, new Device { Id = id, TenantId = tenant, SiteId = site }, CancellationToken.None);

    private static Task PointAsync(string device, long ts, string? area, string tenant = Tenant) =>
        TrackPoint.AppendAsync(tenant, device, [new TrackPoint(device, ts, 45.5, 9.2, 4.5, 0, false, area)], CancellationToken.None);

    private Task<IReadOnlyList<AreaCount>> ComputeAsync(string tenant = Tenant, string site = Site) =>
        _svc.ComputeAsync(tenant, site, Now, CancellationToken.None);

    [Fact]
    public async Task Compute_CountsPerAreaAndListsEmptyOnesAtZero()
    {
        await SeedMapAsync(subAreas: ["work-1", "rest-1"]);
        await AddDeviceAsync("d1");
        await AddDeviceAsync("d2");
        await AddDeviceAsync("d3");
        await PointAsync("d1", Now - Min, "work-1");
        await PointAsync("d2", Now - Min, "work-1");
        await PointAsync("d3", Now - Min, "perimeter");

        Assert.Equal([new("perimeter", 1), new("work-1", 2), new("rest-1", 0)], await ComputeAsync());
    }

    [Fact]
    public async Task Compute_PointAtFourMinutes59Counts_AtFiveMinutesDoesNot()
    {
        await SeedMapAsync();
        await AddDeviceAsync("d1");
        await AddDeviceAsync("d2");
        await PointAsync("d1", Now - 5 * Min + 1000, "perimeter");
        await PointAsync("d2", Now - 5 * Min, "perimeter");

        Assert.Equal([new("perimeter", 1)], await ComputeAsync());
    }

    [Fact]
    public async Task Compute_TwoPointsOfOneDevice_CountsOnceInTheLastArea()
    {
        await SeedMapAsync(subAreas: ["work-1"]);
        await AddDeviceAsync("d1");
        await PointAsync("d1", Now - 2 * Min, "perimeter");
        await PointAsync("d1", Now - Min, "work-1");

        Assert.Equal([new("perimeter", 0), new("work-1", 1)], await ComputeAsync());
    }

    [Fact]
    public async Task Compute_PointAfterNow_IsIgnored()
    {
        await SeedMapAsync();
        await AddDeviceAsync("d1");
        await PointAsync("d1", Now + 1000, "perimeter");

        Assert.Equal([new("perimeter", 0)], await ComputeAsync());
    }

    [Fact]
    public async Task Compute_UnknownAreaId_IsIgnored()
    {
        await SeedMapAsync();
        await AddDeviceAsync("d1");
        await PointAsync("d1", Now - Min, "gone");

        Assert.Equal([new("perimeter", 0)], await ComputeAsync());
    }

    [Fact]
    public async Task Compute_NoMap_ReturnsEmptyList()
    {
        await AddDeviceAsync("d1");
        await PointAsync("d1", Now - Min, "perimeter");

        Assert.Empty(await ComputeAsync());
    }

    [Fact]
    public async Task Compute_OtherSiteAndOtherTenant_NotCounted()
    {
        await SeedMapAsync();
        await SeedMapAsync(site: "site-02");
        await SeedMapAsync(tenant: "tenant-b");
        await AddDeviceAsync("d1", site: "site-02");
        await AddDeviceAsync("d1", tenant: "tenant-b");
        await PointAsync("d1", Now - Min, "perimeter");
        await PointAsync("d1", Now - Min, "perimeter", "tenant-b");

        Assert.Equal([new("perimeter", 0)], await ComputeAsync());
    }

    private sealed class NoopPublisher : IRealtimePublisher
    {
        public Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct) =>
            Task.FromResult(new TenantEvent(1, type, siteId, 0, default));
    }
}
