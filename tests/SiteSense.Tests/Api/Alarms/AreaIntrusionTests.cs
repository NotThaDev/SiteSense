using SiteSense.Api.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Alarms;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Realtime;
using SiteSense.Api.Services;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Alarms;

// AreaIntrusion through the processor (SS-28): entry, exit, replay, late batch, no map, area types, failure.
[Collection("Api")]
public sealed class AreaIntrusionTests : IAsyncLifetime
{
    private const long T0 = 1793268000000;
    private const long Minute = 60_000;
    private const string Tenant = "tenant-a";
    private const string Site = "site-01";
    private const string Dev = "dev-1";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    // Restricted square: lon 9.2000-9.2010, lat 45.5000-45.5010. Perimeter is larger.
    private const double InLon = 9.2005, InLat = 45.5005, OutLon = 9.195, OutLat = 45.5005;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _prevStore = SiteSenseContext.Store;
    private readonly IAppendLog _prevLog = SiteSenseContext.Log;
    private readonly FailAlertStore _store;
    private readonly PositionProcessor _p;

    public AreaIntrusionTests()
    {
        _store = new FailAlertStore(new FileDocumentStore(_root));
        SiteSenseContext.Store = _store;
        SiteSenseContext.Log = new FileAppendLog(_root);
        _p = new PositionProcessor(new NoopPublisher(), new AlarmManager(new NoopPublisher(), NullLogger<AlarmManager>.Instance), NullLogger<PositionProcessor>.Instance);
    }

    public async Task InitializeAsync()
    {
        await Device.CreateAsync(Tenant, new Device { Id = Dev, TenantId = Tenant, SiteId = Site }, CancellationToken.None);
        await _p.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _p.StopAsync(CancellationToken.None);
        SiteSenseContext.Store = _prevStore;
        SiteSenseContext.Log = _prevLog;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static Task SaveMapAsync(string areaType = "restricted") => SiteSenseContext.Store.SaveAsync(Tenant, new SiteMap
    {
        Id = Site,
        TenantId = Tenant,
        Areas =
        [
            new("perimeter", "Site", "perimeter", null, [[[9.19, 45.49], [9.21, 45.49], [9.21, 45.51], [9.19, 45.51], [9.19, 45.49]]]),
            new("area-1", "Zone", areaType, null, [[[9.2, 45.5], [9.201, 45.5], [9.201, 45.501], [9.2, 45.501], [9.2, 45.5]]]),
        ],
    }, CancellationToken.None);

    // minute offset, in or out of the area
    private async Task SendAsync(params (int Min, bool In)[] pts)
    {
        await Position.AppendNewAsync(Tenant, Dev,
            pts.Select(x => new Position(Dev, T0 + x.Min * Minute, x.In ? InLat : OutLat, x.In ? InLon : OutLon, 4.5)).ToList(), CancellationToken.None);
        await DeviceProcessing.MarkDirtyAsync(Tenant, Dev, T0 + pts.Min(x => x.Min) * Minute, CancellationToken.None);
        _p.Enqueue(Tenant, Dev);
    }

    private Task<bool> DrainAsync() => _p.DrainAsync(Tenant, CancellationToken.None).WaitAsync(Limit);

    private async Task<List<Alert>> AlertsAsync() =>
        (await Alert.ListAsync(Tenant, Site, CancellationToken.None)).OrderBy(a => a.EventTimeMs).ToList();

    [Fact]
    public async Task WalkInOutIn_TwoAlarms_FirstNotActiveAfterExit()
    {
        await SaveMapAsync();
        await SendAsync((1, false), (2, true), (3, true), (4, false), (5, true));
        Assert.True(await DrainAsync());

        var alerts = await AlertsAsync();
        Assert.Equal(2, alerts.Count);
        Assert.Equal(T0 + 2 * Minute, alerts[0].EventTimeMs);
        Assert.Equal(AlertStates.NotActive, alerts[0].State);
        Assert.Equal(T0 + 4 * Minute, alerts[0].EndTimeMs);
        Assert.Equal(T0 + 5 * Minute, alerts[1].EventTimeMs);
        Assert.Equal(AlertStates.Active, alerts[1].State);
        Assert.All(alerts, a => Assert.False(a.Late));
        Assert.All(alerts, a => Assert.Equal("area-1", a.AreaId));
    }

    [Fact]
    public async Task FirstPointInside_IsAnEntry_AndNextBatchContinuesTheStay()
    {
        await SaveMapAsync();
        await SendAsync((1, true));
        Assert.True(await DrainAsync());
        await SendAsync((2, true), (3, true));
        Assert.True(await DrainAsync());

        Assert.Single(await AlertsAsync());
    }

    [Fact]
    public async Task ReplayOfTheSameBatch_AddsNoAlarm()
    {
        await SaveMapAsync();
        await SendAsync((1, false), (2, true), (3, false));
        Assert.True(await DrainAsync());
        await SendAsync((1, false), (2, true), (3, false));
        Assert.True(await DrainAsync());

        Assert.Single(await AlertsAsync());
    }

    [Fact]
    public async Task LateBatch_IsMarkedLate()
    {
        await SaveMapAsync();
        await SendAsync((10, false));
        Assert.True(await DrainAsync());
        await SendAsync((5, false), (6, true));
        Assert.True(await DrainAsync());

        var alert = Assert.Single(await AlertsAsync());
        Assert.True(alert.Late);
        Assert.Equal(T0 + 6 * Minute, alert.EventTimeMs);
    }

    [Fact]
    public async Task SiteWithoutMap_NoAlarm()
    {
        await SendAsync((1, true), (2, true));
        Assert.True(await DrainAsync());

        Assert.Empty(await AlertsAsync());
    }

    [Theory]
    [InlineData("work")]
    [InlineData("hazard", true)]
    public async Task AreaType_OnlyRestrictedAndHazardRaise(string areaType, bool expectAlarm = false)
    {
        await SaveMapAsync(areaType);
        await SendAsync((1, false), (2, true));
        Assert.True(await DrainAsync());

        Assert.Equal(expectAlarm ? 1 : 0, (await AlertsAsync()).Count);
    }

    [Fact]
    public async Task DetectorFailure_KeepsTheDirtyMarker_AndRetryRaisesTheAlarmOnce()
    {
        await SaveMapAsync();
        _store.FailAlertSave = true;
        await SendAsync((1, false), (2, true));
        Assert.False(await DrainAsync());
        Assert.NotNull((await DeviceProcessing.GetAsync(Tenant, Dev, CancellationToken.None))!.DirtyFromMs);

        _store.FailAlertSave = false;
        _p.Enqueue(Tenant, Dev);
        Assert.True(await DrainAsync());

        Assert.Single(await AlertsAsync());
        Assert.Null((await DeviceProcessing.GetAsync(Tenant, Dev, CancellationToken.None))!.DirtyFromMs);
    }

    private sealed class NoopPublisher : IRealtimePublisher
    {
        public Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct) =>
            Task.FromResult(new TenantEvent(1, type, siteId, 0, default));
    }

    private sealed class FailAlertStore(IDocumentStore inner) : IDocumentStore
    {
        public bool FailAlertSave { get; set; }

        public Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument => inner.GetAsync<T>(tenantId, id, ct);

        public Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument =>
            FailAlertSave && doc is Alert ? throw new IOException("disk") : inner.SaveAsync(tenantId, doc, ct);

        public Task<IReadOnlyList<T>> ListAsync<T>(string tenantId, CancellationToken ct) where T : class, IDocument => inner.ListAsync<T>(tenantId, ct);

        public Task<bool> DeleteAsync<T>(string tenantId, string id, long expectedVersion, CancellationToken ct) where T : class, IDocument =>
            inner.DeleteAsync<T>(tenantId, id, expectedVersion, ct);
    }
}
