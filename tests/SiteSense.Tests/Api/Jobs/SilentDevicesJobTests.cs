using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Alarms;
using SiteSense.Api.BackgroundJobs;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Realtime;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Jobs;

// DeviceSilent (SS-30): threshold, one alarm per episode, entrance exception, end on new data, tenant isolation, endpoint.
// Api collection: the clock is a static shared by every test that reads it. Tenants come from the test host (SilentAfterMinutes = 10).
[Collection("Api")]
public sealed class SilentDevicesJobTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268000000;
    private const long MinMs = 60_000;
    private const string Tenant = "tenant-a";
    // Entrance at lon 9.2000: 0.0001 deg is about 8 m, 0.001 deg about 78 m.
    private const double EntranceLon = 9.2000, Lat = 45.5000;
    private const double NearLon = 9.20010, FarLon = 9.2010;

    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;
    private readonly RecordingPublisher _publisher = new();
    private readonly SilentDevicesJob _job;
    // Device ids are unique per tenant (Q13) and the store is shared, so each test gets its own.
    private readonly string _site = $"site-{Guid.NewGuid():N}";
    private string Dev => $"{_site}-d";

    public SilentDevicesJobTests(TestModeApiFactory factory)
    {
        _factory = factory;
        // Build the host first: its startup sets the static clock and the tenants.
        _ = _factory.Services;
        SetNow(NowMs);
        _job = new SilentDevicesJob(new AlarmManager(_publisher, NullLogger<AlarmManager>.Instance, Microsoft.Extensions.Options.Options.Create(new AlarmOptions())));
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    private static void SetNow(long ms) => SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(ms));

    [Fact]
    public async Task Run_SilentLongerThanThreshold_RaisesOneDeviceSilentAtLastSeen()
    {
        // Arrange
        var lastSeen = NowMs - 11 * MinMs;
        await AddDeviceAsync(Dev, Tenant, lastSeen);

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);

        // Assert
        var alert = Assert.Single(await AlertsAsync());
        Assert.Equal(AlertTypes.DeviceSilent, alert.Type);
        Assert.Equal(Dev, alert.DeviceId);
        Assert.Equal(lastSeen, alert.EventTimeMs);
        Assert.Equal("perimeter", alert.AreaId);
        Assert.False(alert.Late);
        Assert.Equal([EventTypes.AlertRaised], _publisher.Types);
    }

    [Fact]
    public async Task Run_SilentShorterThanThreshold_RaisesNothing()
    {
        // Arrange
        await AddDeviceAsync(Dev, Tenant, NowMs - 9 * MinMs);

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);

        // Assert
        Assert.Empty(await AlertsAsync());
    }

    [Fact]
    public async Task Run_RepeatedAtLaterMinutes_RaisesOneAlertAndOneEvent()
    {
        // Arrange
        await AddDeviceAsync(Dev, Tenant, NowMs - 11 * MinMs);

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);
        SetNow(NowMs + 5 * MinMs);
        await _job.RunAsync(Tenant, _site, NowMs + 5 * MinMs, CancellationToken.None);
        await _job.RunAsync(Tenant, _site, NowMs + 5 * MinMs, CancellationToken.None);

        // Assert
        Assert.Single(await AlertsAsync());
        Assert.Single(_publisher.Types);
    }

    [Fact]
    public async Task Run_LastPositionNearEntrance_RaisesNothing()
    {
        // Arrange
        await AddDeviceAsync(Dev, Tenant, NowMs - 30 * MinMs, lon: NearLon);

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);

        // Assert
        Assert.Empty(await AlertsAsync());
    }

    [Fact]
    public async Task Run_LastPositionFarFromEntrance_RaisesAlarm()
    {
        // Arrange
        await AddDeviceAsync(Dev, Tenant, NowMs - 30 * MinMs, lon: FarLon);

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);

        // Assert
        Assert.Single(await AlertsAsync());
    }

    [Fact]
    public async Task Run_DeviceTransmitsAgain_EndsAlertAndSecondSilenceIsNewAlarm()
    {
        // Arrange
        await AddDeviceAsync(Dev, Tenant, NowMs - 11 * MinMs, lon: FarLon);
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);
        var firstId = Assert.Single(await AlertsAsync()).Id;

        // Act: it transmits again, then goes silent again.
        var back = NowMs + MinMs;
        await TrackPoint.AppendAsync(Tenant, Dev, [new TrackPoint(Dev, back, Lat, FarLon, 4.5, 0)], CancellationToken.None);
        SetNow(back + MinMs);
        await _job.RunAsync(Tenant, _site, back + MinMs, CancellationToken.None);
        var afterReturn = await AlertsAsync();
        SetNow(back + 11 * MinMs);
        await _job.RunAsync(Tenant, _site, back + 11 * MinMs, CancellationToken.None);

        // Assert
        var ended = Assert.Single(afterReturn);
        Assert.Equal(AlertStates.NotActive, ended.State);
        Assert.Equal(back, ended.EndTimeMs);
        var all = await AlertsAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(1, all.Count(a => a.State == AlertStates.Active && a.EventTimeMs == back && a.Id != firstId));
    }

    [Fact]
    public async Task Run_OtherTenantsDeviceAndOtherSite_AreIgnored()
    {
        // Arrange
        await AddDeviceAsync($"{_site}-ot", "tenant-b", NowMs - 30 * MinMs, siteId: _site);
        await AddDeviceAsync($"{_site}-os", Tenant, NowMs - 30 * MinMs, siteId: $"{_site}-x");

        // Act
        await _job.RunAsync(Tenant, _site, NowMs, CancellationToken.None);

        // Assert
        Assert.Empty(await AlertsAsync());
        Assert.Empty(await Alert.ListAsync("tenant-b", _site, CancellationToken.None));
    }

    [Fact]
    public async Task RunEndpoint_SilentDevice_Returns204AndRaisesAlarm()
    {
        // Arrange
        await Site.CreateAsync(Tenant, new Site { Id = _site, TenantId = Tenant, Name = "S", TimeZone = "UTC", Navigation = new NavigationSettings(1.4, 0.5, 0.5, 1, 5) }, CancellationToken.None);
        await AddDeviceAsync(Dev, Tenant, NowMs - 11 * MinMs);
        var admin = _factory.CreateClient();
        admin.WithToken(TestTokens.Create(Tenant, ["Admin"]));

        // Act
        var response = await admin.PostAsync("/api/v1/test/jobs/silent-devices/run", null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Single(await AlertsAsync());
    }

    private async Task AddDeviceAsync(string deviceId, string tenant, long lastSeenMs, double lon = FarLon, string? siteId = null)
    {
        var site = siteId ?? _site;
        await Device.CreateAsync(tenant, new Device { Id = deviceId, TenantId = tenant, SiteId = site }, CancellationToken.None);
        if (await SiteMap.GetAsync(tenant, site, CancellationToken.None) is null) await SiteSenseContext.Store.SaveAsync(tenant, Map(tenant, site), CancellationToken.None);
        await TrackPoint.AppendAsync(tenant, deviceId, [new TrackPoint(deviceId, lastSeenMs, Lat, lon, 4.5, 0, AreaId: "perimeter")], CancellationToken.None);
    }

    private static SiteMap Map(string tenant, string site) => new()
    {
        Id = site,
        TenantId = tenant,
        Areas = [new("perimeter", "Site", "perimeter", null, [[[9.19, 45.49], [9.21, 45.49], [9.21, 45.51], [9.19, 45.51], [9.19, 45.49]]])],
        Entrances = [new("gate", "Gate", [EntranceLon, Lat])],
    };

    private async Task<List<Alert>> AlertsAsync() =>
        (await Alert.ListAsync(Tenant, _site, CancellationToken.None)).OrderBy(a => a.EventTimeMs).ToList();

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<string> Types { get; } = [];

        public Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct)
        {
            Types.Add(type);
            return Task.FromResult(new TenantEvent(0, type, siteId, 0, default));
        }
    }
}
