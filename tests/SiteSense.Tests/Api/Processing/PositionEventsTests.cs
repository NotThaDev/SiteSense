using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Auth;
using SiteSense.Api.Alarms;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Realtime;
using SiteSense.Api.Services;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Processing;

// PositionUpdated from the processor (SS-50, Q115): once per position, after the save, also on replay and failure paths.
[Collection("Api")]
public sealed class PositionEventsTests : IAsyncLifetime
{
    private const long T0 = 1793268000000;
    private const string Tenant = "tenant-a";
    private const string Dev = "dev-1";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static readonly string[] Safety = [SiteSensePolicies.CanSeeIndividuals];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _prevStore = SiteSenseContext.Store;
    private readonly IAppendLog _prevLog = SiteSenseContext.Log;
    private readonly HookStore _store;
    private readonly TestPublisher _publisher = new(new EventPublisher(new NullEventSender(), NullLogger<EventPublisher>.Instance));
    private readonly PositionProcessor _p;

    public PositionEventsTests()
    {
        _store = new HookStore(new FileDocumentStore(_root));
        SiteSenseContext.Store = _store;
        SiteSenseContext.Log = new FileAppendLog(_root);
        _p = new PositionProcessor(_publisher, new AlarmManager(_publisher, NullLogger<AlarmManager>.Instance), NullLogger<PositionProcessor>.Instance);
    }

    public async Task InitializeAsync()
    {
        await Device.CreateAsync(Tenant, new Device { Id = Dev, TenantId = Tenant, SiteId = "site-01" }, CancellationToken.None);
        await _p.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _p.StopAsync(CancellationToken.None);
        SiteSenseContext.Store = _prevStore;
        SiteSenseContext.Log = _prevLog;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static async Task MarkAsync(bool enqueue, PositionProcessor p, params long[] ts)
    {
        await Position.AppendNewAsync(Tenant, Dev, ts.Select(t => new Position(Dev, T0 + t, 45.5, 9.2, 4.5)).ToList(), CancellationToken.None);
        await DeviceProcessing.MarkDirtyAsync(Tenant, Dev, T0 + ts.Min(), CancellationToken.None);
        if (enqueue) p.Enqueue(Tenant, Dev);
    }

    private Task IngestAsync(params long[] ts) => MarkAsync(true, _p, ts);

    private Task<bool> DrainAsync() => _p.DrainAsync(Tenant, CancellationToken.None).WaitAsync(Limit);

    private async Task<IReadOnlyList<TenantEvent>> EventsAsync(string[]? policies = null) =>
        (await _publisher.Inner.ReadAfterAsync(Tenant, 0, policies ?? Safety, CancellationToken.None)).Items;

    private async Task<List<long>> TimestampsAsync() =>
        (await EventsAsync()).Select(e => e.Data.GetProperty("timestampMs").GetInt64()).ToList();

    [Fact]
    public async Task Batch_PublishesOnePositionUpdatedPerPosition()
    {
        await IngestAsync(3000, 1000, 2000);
        Assert.True(await DrainAsync());

        var events = await EventsAsync();

        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal(EventTypes.PositionUpdated, e.Type));
        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000], events.Select(e => e.Data.GetProperty("timestampMs").GetInt64()));
        var d = events[0].Data;
        Assert.Equal(Dev, d.GetProperty("deviceId").GetString());
        Assert.Equal(45.5, d.GetProperty("lat").GetDouble());
        Assert.Equal(9.2, d.GetProperty("lon").GetDouble());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, d.GetProperty("areaId").ValueKind);
    }

    [Fact]
    public async Task DuplicateBatch_PublishesNothingNew()
    {
        await IngestAsync(1000, 2000);
        Assert.True(await DrainAsync());

        await IngestAsync(1000, 2000);
        Assert.True(await DrainAsync());

        Assert.Equal([T0 + 1000, T0 + 2000], await TimestampsAsync());
    }

    [Fact]
    public async Task LateBatch_PublishesOnlyTheLatePositions()
    {
        await IngestAsync(1000, 2000, 3000);
        Assert.True(await DrainAsync());

        await IngestAsync(500, 1500);
        Assert.True(await DrainAsync());

        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000, T0 + 500, T0 + 1500], await TimestampsAsync());
    }

    [Fact]
    public async Task Events_GoToTheDeviceSite()
    {
        await IngestAsync(1000);
        Assert.True(await DrainAsync());

        Assert.Equal("site-01", Assert.Single(await EventsAsync()).SiteId);
    }

    [Fact]
    public async Task Viewer_DoesNotSeePositionUpdatedInEvents()
    {
        await IngestAsync(1000);
        Assert.True(await DrainAsync());

        Assert.Empty(await EventsAsync([SiteSensePolicies.CanSeeAggregates]));
    }

    [Fact]
    public async Task ConcurrentMark_DoesNotPublishTwice()
    {
        var first = true;
        _store.BeforeSave = async () =>
        {
            if (!first) return;
            first = false;
            await MarkAsync(false, _p, 2000); // lands mid-run: the processor's save conflicts and it runs again
        };
        await IngestAsync(1000);

        Assert.True(await DrainAsync());

        Assert.Equal([T0 + 1000, T0 + 2000], await TimestampsAsync());
    }

    [Fact]
    public async Task FailedSave_RetryPublishesTheLatePositionsOnce()
    {
        await IngestAsync(1000, 2000, 3000);
        Assert.True(await DrainAsync());
        await MarkAsync(false, _p, 500);
        _store.BeforeSave = () => throw new IOException("disk"); // armed after the mark: only the processor's save fails
        _p.Enqueue(Tenant, Dev);
        Assert.False(await DrainAsync());
        Assert.Equal(3, (await EventsAsync()).Count); // the failed attempt published nothing
        _store.BeforeSave = null;

        _p.Enqueue(Tenant, Dev);
        Assert.True(await DrainAsync());

        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000, T0 + 500], await TimestampsAsync());
    }

    [Fact]
    public async Task PublishFailure_DoesNotFailTheRun()
    {
        _publisher.Throw = true;
        await IngestAsync(1000, 2000);

        Assert.True(await DrainAsync());

        Assert.Equal(2, (await TrackPoint.ReadAsync(Tenant, Dev, 0, long.MaxValue, CancellationToken.None)).Count);
        Assert.Null((await DeviceProcessing.GetAsync(Tenant, Dev, CancellationToken.None))!.DirtyFromMs);
    }

    private sealed class TestPublisher(EventPublisher events) : IRealtimePublisher
    {
        public EventPublisher Inner { get; } = events;
        public bool Throw { get; set; }

        public Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct) =>
            Throw ? throw new IOException("event log") : Inner.PublishAsync(tenantId, siteId, type, data, ct);
    }

    // BeforeSave runs before every DeviceProcessing save of the processor or a mark.
    private sealed class HookStore(IDocumentStore inner) : IDocumentStore
    {
        public Func<Task>? BeforeSave { get; set; }

        public Task<T?> GetAsync<T>(string tenantId, string id, CancellationToken ct) where T : class, IDocument => inner.GetAsync<T>(tenantId, id, ct);

        public async Task<T> SaveAsync<T>(string tenantId, T doc, CancellationToken ct) where T : class, IDocument
        {
            if (doc is DeviceProcessing && BeforeSave is { } hook) await hook();
            return await inner.SaveAsync(tenantId, doc, ct);
        }

        public Task<IReadOnlyList<T>> ListAsync<T>(string tenantId, CancellationToken ct) where T : class, IDocument => inner.ListAsync<T>(tenantId, ct);

        public Task<bool> DeleteAsync<T>(string tenantId, string id, long expectedVersion, CancellationToken ct) where T : class, IDocument =>
            inner.DeleteAsync<T>(tenantId, id, expectedVersion, ct);
    }
}
