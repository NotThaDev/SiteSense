using SiteSense.Api.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        private const string Tenant = "tenant-a";
    private const string Site = "site-01";
    private const string Dev = "dev-1";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    // Restricted square: lon 9.2000-9.2010, lat 45.5000-45.5010. Perimeter is larger.
    // 8 m inside / 8 m outside the west edge: close enough to pass the plausibility filter (VMaxMps, SS-24) at 5 s steps.
    private const double InLon = 9.20010, InLat = 45.5005, OutLon = 9.19990, OutLat = 45.5005;

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
        _p = new PositionProcessor(new NoopPublisher(), new AlarmManager(new NoopPublisher(), NullLogger<AlarmManager>.Instance), NullLogger<PositionProcessor>.Instance, Options.Create(new TrackingOptions()));
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

    // Samples every 10 s from..to (seconds), all inside or all outside the area.
    private static (int Sec, bool In)[] Run(int from, int to, bool inside) =>
        Enumerable.Range(0, (to - from) / 10 + 1).Select(i => (from + i * 10, inside)).ToArray();

    // seconds offset, in or out of the area
    private async Task SendAsync(params (int Sec, bool In)[] pts)
    {
        await Position.AppendNewAsync(Tenant, Dev,
            pts.Select(x => new Position(Dev, T0 + x.Sec * 1000L, x.In ? InLat : OutLat, x.In ? InLon : OutLon, 4.5)).ToList(), CancellationToken.None);
        await DeviceProcessing.MarkDirtyAsync(Tenant, Dev, T0 + pts.Min(x => x.Sec) * 1000L, CancellationToken.None);
        _p.Enqueue(Tenant, Dev);
    }

    private Task<bool> DrainAsync() => _p.DrainAsync(Tenant, CancellationToken.None).WaitAsync(Limit);

    private async Task<List<Alert>> AlertsAsync() =>
        (await Alert.ListAsync(Tenant, Site, CancellationToken.None)).OrderBy(a => a.EventTimeMs).ToList();

    private async Task<List<Alert>> SendAndDrainAsync(params (int Sec, bool In)[] pts)
    {
        await SendAsync(pts);
        Assert.True(await DrainAsync());
        return await AlertsAsync();
    }

    [Fact]
    public async Task StayOf40Seconds_RaisesOneActiveAlarmAtTheFirstInsidePoint()
    {
        await SaveMapAsync();
        var alert = Assert.Single(await SendAndDrainAsync([(0, false), .. Run(10, 50, true)]));
        Assert.Equal(T0 + 10_000, alert.EventTimeMs);
        Assert.Equal(AlertStates.Active, alert.State);
        Assert.False(alert.Late);
        Assert.Equal("area-1", alert.AreaId);
    }

    [Fact]
    public async Task StayOf20Seconds_ThenOut_NoAlarm()
    {
        await SaveMapAsync();
        Assert.Empty(await SendAndDrainAsync([(0, false), .. Run(10, 30, true), .. Run(40, 100, false)]));
    }

    [Fact]
    public async Task BoundaryJitterWithin30Seconds_NoAlarm()
    {
        await SaveMapAsync();
        Assert.Empty(await SendAndDrainAsync((0, false), (5, true), (10, false), (15, true), (20, false), (25, true), (30, false)));
    }

    [Fact]
    public async Task ShortOutsideBlipInsideAStay_KeepsTheSameActiveAlarm()
    {
        await SaveMapAsync();
        var alert = Assert.Single(await SendAndDrainAsync([.. Run(0, 40, true), (50, false), .. Run(60, 100, true)]));
        Assert.Equal(AlertStates.Active, alert.State);
        Assert.Equal(T0, alert.EventTimeMs);
    }

    [Fact]
    public async Task Exit_After40SecondsOutside_DeactivatesAtTheFirstOutsideKeptPoint()
    {
        await SaveMapAsync();
        var alert = Assert.Single(await SendAndDrainAsync([.. Run(0, 40, true), .. Run(50, 90, false)]));
        Assert.Equal(AlertStates.NotActive, alert.State);
        // The track filter averages each kept point with the previous one (SS-24): the first outside sample lands on the edge, still inside; the next is outside.
        Assert.Equal(T0 + 60_000, alert.EndTimeMs);
    }

    [Fact]
    public async Task OutsideOnly20Seconds_ThenBackIn_AlarmStaysActive()
    {
        await SaveMapAsync();
        var alert = Assert.Single(await SendAndDrainAsync([.. Run(0, 40, true), .. Run(50, 70, false), .. Run(80, 120, true)]));
        Assert.Equal(AlertStates.Active, alert.State);
        Assert.Null(alert.EndTimeMs);
    }

    [Fact]
    public async Task InOutIn_TwoAlarms_FirstNotActiveSecondActive_NeverReopened()
    {
        await SaveMapAsync();
        var alerts = await SendAndDrainAsync([.. Run(0, 40, true), .. Run(50, 90, false), .. Run(100, 140, true)]);

        Assert.Equal(2, alerts.Count);
        Assert.Equal(T0, alerts[0].EventTimeMs);
        Assert.Equal(AlertStates.NotActive, alerts[0].State);
        Assert.Equal(T0 + 60_000, alerts[0].EndTimeMs); // first outside sample is smoothed onto the edge (SS-24)
        Assert.Equal(T0 + 100_000, alerts[1].EventTimeMs);
        Assert.Equal(AlertStates.Active, alerts[1].State);
    }

    [Fact]
    public async Task ReplayOfTheSameBatch_AddsNothingAndChangesNothing()
    {
        await SaveMapAsync();
        (int, bool)[] batch = [.. Run(0, 40, true), .. Run(50, 90, false), .. Run(100, 140, true)];
        var before = await SendAndDrainAsync(batch);
        var after = await SendAndDrainAsync(batch);

        Assert.Equal(2, after.Count);
        Assert.Equal(before.Select(a => (a.Id, a.State, a.EndTimeMs)), after.Select(a => (a.Id, a.State, a.EndTimeMs)));
    }

    [Fact]
    public async Task ConfirmationSplitAcrossTwoBatches_RaisesOnceInBatch2WithTheEntryTimeOfBatch1()
    {
        await SaveMapAsync();
        Assert.Empty(await SendAndDrainAsync((0, false), (10, true), (20, true)));
        var alert = Assert.Single(await SendAndDrainAsync((30, true), (40, true), (50, true)));
        Assert.Equal(T0 + 10_000, alert.EventTimeMs);
        Assert.Equal(AlertStates.Active, alert.State);
        Assert.False(alert.Late);
    }

    [Fact]
    public async Task LateBatch_IsMarkedLate()
    {
        await SaveMapAsync();
        await SendAsync((600, false));
        Assert.True(await DrainAsync());
        await SendAsync((300, false), (360, true), (400, true));
        Assert.True(await DrainAsync());

        var alert = Assert.Single(await AlertsAsync());
        Assert.True(alert.Late);
        Assert.Equal(T0 + 360_000, alert.EventTimeMs);
    }

    [Fact]
    public async Task SiteWithoutMap_NoAlarm()
    {
        await SendAsync(Run(0, 40, true));
        Assert.True(await DrainAsync());

        Assert.Empty(await AlertsAsync());
    }

    [Theory]
    [InlineData("work")]
    [InlineData("hazard", true)]
    public async Task AreaType_OnlyRestrictedAndHazardRaise(string areaType, bool expectAlarm = false)
    {
        await SaveMapAsync(areaType);
        await SendAsync([(0, false), .. Run(10, 50, true)]);
        Assert.True(await DrainAsync());

        Assert.Equal(expectAlarm ? 1 : 0, (await AlertsAsync()).Count);
    }

    [Fact]
    public async Task DetectorFailure_KeepsTheDirtyMarker_AndRetryRaisesTheAlarmOnce()
    {
        await SaveMapAsync();
        _store.FailAlertSave = true;
        await SendAsync([(0, false), .. Run(10, 50, true)]);
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
