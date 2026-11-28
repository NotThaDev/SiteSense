using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SiteSense.Api.Alarms;
using SiteSense.Api.Helpers;
using SiteSense.Api.Realtime;

namespace SiteSense.Tests.Api.Alarms;

[Collection("Api")]
public sealed class AlarmManagerTests : IClassFixture<ApiFactory>, IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly RecordingPublisher _publisher = new();
    private readonly AlarmManager _manager;

    public AlarmManagerTests(ApiFactory factory)
    {
        // Starting the host sets the store that Alert saves to.
        _manager = new AlarmManager(_publisher, NullLogger<AlarmManager>.Instance, Options.Create(new AlarmOptions()));
        _ = factory.Services;
    }

    [Fact]
    public async Task Notify_TwiceSameAlarm_SavesOnceAndPublishesOnce()
    {
        var site = NewSiteId();

        var first = await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-1", "area-1", 1000, false, CancellationToken.None);
        var second = await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-1", "area-1", 1000, false, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(new[] { EventTypes.AlertRaised }, _publisher.Types);
        Assert.Single(await Alert.ListAsync(Tenant, site, CancellationToken.None));
    }

    private const long Event = 1_000_000;
    private readonly TimeProvider _prevTime = SiteSenseContext.Time;

    public void Dispose() => SiteSenseContext.Time = _prevTime;

    private async Task<(bool Late, bool Created)> NotifyAtAsync(long nowMs, bool replay, string site)
    {
        SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(nowMs));
        var created = await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-l", "area-1", Event, replay, CancellationToken.None);
        var stored = await Alert.GetAsync(Tenant, Alert.IdFor(AlertTypes.AreaIntrusion, site, "dev-l", "area-1", Event), CancellationToken.None);
        return (stored!.Late, created);
    }

    [Fact]
    public async Task Notify_DetectedAfterThreshold_IsLate()
    {
        var (late, _) = await NotifyAtAsync(Event + 60_001, false, NewSiteId());
        Assert.True(late);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59_999)]
    [InlineData(60_000)]
    public async Task Notify_WithinOrAtThreshold_IsNotLate(long delayMs)
    {
        var (late, _) = await NotifyAtAsync(Event + delayMs, false, NewSiteId());
        Assert.False(late);
    }

    [Fact]
    public async Task Notify_Replay_IsLateEvenWhenFresh()
    {
        var (late, _) = await NotifyAtAsync(Event, true, NewSiteId());
        Assert.True(late);
    }

    [Fact]
    public async Task Notify_DuplicateAfterLate_CreatesAndPublishesNothing_KeepsEventTimeAndFlag()
    {
        var site = NewSiteId();
        await NotifyAtAsync(Event, false, site);
        var (late, created) = await NotifyAtAsync(Event + 600_000, false, site);

        Assert.False(created);
        Assert.False(late);
        Assert.Equal(new[] { EventTypes.AlertRaised }, _publisher.Types);
        var stored = Assert.Single(await Alert.ListAsync(Tenant, site, CancellationToken.None));
        Assert.Equal(Event, stored.EventTimeMs);
    }

    [Fact]
    public async Task Notify_PublishesOnlyAfterSave()
    {
        var site = NewSiteId();

        await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-2", "area-1", 2000, true, CancellationToken.None);

        Assert.True(_publisher.StoredAtPublish);
    }

    [Fact]
    public async Task Notify_PublishFails_ReturnsTrueAndKeepsAlert()
    {
        var site = NewSiteId();
        _publisher.Fail = true;

        var created = await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-3", "area-1", 3000, false, CancellationToken.None);

        Assert.True(created);
        Assert.Single(await Alert.ListAsync(Tenant, site, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_ActiveAlert_SetsNotActiveAndPublishesOnce()
    {
        var site = NewSiteId();
        await _manager.NotifyAlarmAsync(Tenant, site, AlertTypes.AreaIntrusion, "dev-4", "area-1", 4000, false, CancellationToken.None);
        var id = Alert.IdFor(AlertTypes.AreaIntrusion, site, "dev-4", "area-1", 4000);

        var ended = await _manager.DeactivateAlarmAsync(Tenant, id, 4500, CancellationToken.None);
        var again = await _manager.DeactivateAlarmAsync(Tenant, id, 4600, CancellationToken.None);

        Assert.True(ended);
        Assert.False(again);
        var stored = await Alert.GetAsync(Tenant, id, CancellationToken.None);
        Assert.Equal(AlertStates.NotActive, stored!.State);
        Assert.Equal(4500, stored.EndTimeMs);
        Assert.Equal(new[] { EventTypes.AlertRaised, EventTypes.AlertUpdated }, _publisher.Types);
    }

    [Fact]
    public async Task Deactivate_UnknownAlert_ReturnsFalseAndPublishesNothing()
    {
        var ended = await _manager.DeactivateAlarmAsync(Tenant, "unknown-alert", 1, CancellationToken.None);

        Assert.False(ended);
        Assert.Empty(_publisher.Types);
    }

    private static string NewSiteId() => $"site-{Guid.NewGuid():N}";

    private sealed class RecordingPublisher : IRealtimePublisher
    {
        public List<string> Types { get; } = [];
        public bool Fail { get; set; }
        public bool StoredAtPublish { get; private set; }

        public async Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct)
        {
            if (Fail) throw new InvalidOperationException("bus down");
            if (data is AlertRaisedData raised)
                StoredAtPublish = await Alert.GetAsync(tenantId, raised.AlertId, ct) is not null;
            Types.Add(type);
            return new TenantEvent(0, type, siteId, 0, default);
        }
    }
}
