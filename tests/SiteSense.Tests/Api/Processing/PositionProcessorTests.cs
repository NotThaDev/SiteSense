using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Services;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Processing;

[Collection("Api")]
public sealed class PositionProcessorTests : IAsyncLifetime
{
    private const long T0 = 1793268000000; // aligned to a UTC hour boundary
    private const long Minute = 60_000;
    private const string TenantA = "tenant-a";
    private const string Dev = "dev-1";
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _prevStore = SiteSenseContext.Store;
    private readonly IAppendLog _prevLog = SiteSenseContext.Log;
    private readonly FakeLog _log;
    private readonly PositionProcessor _p = new(NullLogger<PositionProcessor>.Instance);

    public PositionProcessorTests()
    {
        _log = new FakeLog(new FileAppendLog(_root));
        SiteSenseContext.Store = new FileDocumentStore(_root);
        SiteSenseContext.Log = _log;
    }

    public Task InitializeAsync() => _p.StartAsync(CancellationToken.None);

    public async Task DisposeAsync()
    {
        await _p.StopAsync(CancellationToken.None);
        SiteSenseContext.Store = _prevStore;
        SiteSenseContext.Log = _prevLog;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static async Task IngestAsync(PositionProcessor? p, string tenant, params long[] ts)
    {
        var positions = ts.Select(t => new Position(Dev, T0 + t, 45.5, 9.2, 4.5)).ToList();
        await Position.AppendNewAsync(tenant, Dev, positions, CancellationToken.None);
        await DeviceProcessing.MarkDirtyAsync(tenant, Dev, T0 + ts.Min(), CancellationToken.None);
        p?.Enqueue(tenant, Dev);
    }

    private Task IngestAsync(params long[] ts) => IngestAsync(_p, TenantA, ts);

    private Task<bool> DrainAsync(string tenant = TenantA) => _p.DrainAsync(tenant, CancellationToken.None).WaitAsync(Limit);

    private static Task<IReadOnlyList<TrackPoint>> TrackAsync(string tenant = TenantA) =>
        TrackPoint.ReadAsync(tenant, Dev, 0, long.MaxValue, CancellationToken.None);

    [Fact]
    public async Task Drain_OutOfOrderBatch_TrackInTimestampOrder()
    {
        await IngestAsync(3000, 1000, 2000);

        Assert.True(await DrainAsync());

        var track = await TrackAsync();
        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000], track.Select(t => t.TimestampMs));
        Assert.All(track, t => Assert.Equal(0L, t.Revision));
        var doc = await DeviceProcessing.GetAsync(TenantA, Dev, CancellationToken.None);
        Assert.Equal(T0 + 3000, doc!.CursorMs);
        Assert.Null(doc.DirtyFromMs);
    }

    [Fact]
    public async Task Drain_LatePosition_ReplaysWithNewRevision()
    {
        await IngestAsync(Enumerable.Range(0, 6).Select(i => i * Minute).ToArray());
        Assert.True(await DrainAsync());
        var cursor = (await DeviceProcessing.GetAsync(TenantA, Dev, CancellationToken.None))!.CursorMs;

        await IngestAsync(-150 * Minute);
        Assert.True(await DrainAsync());

        var track = await TrackAsync();
        Assert.Equal(7, track.Count);
        Assert.All(track, t => Assert.Equal(1L, t.Revision));
        Assert.Equal(T0 - 150 * Minute, track[0].TimestampMs);
        Assert.Equal(cursor, (await DeviceProcessing.GetAsync(TenantA, Dev, CancellationToken.None))!.CursorMs);
    }

    [Fact]
    public async Task Drain_SameBatchTwice_EachTimestampOnce()
    {
        await IngestAsync(1000, 2000);
        await IngestAsync(1000, 2000);

        Assert.True(await DrainAsync());

        Assert.Equal(2, (await TrackAsync()).Count);
    }

    [Fact]
    public async Task Drain_NothingPending_ReturnsTrueAtOnce() => Assert.True(await DrainAsync());

    [Fact]
    public async Task Drain_OtherTenantPending_DoesNotWait()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _log.OnReadPositions = tenant =>
        {
            if (tenant != "tenant-b") return Task.CompletedTask;
            started.TrySetResult();
            return release.Task;
        };
        await IngestAsync(_p, "tenant-b", 1000);
        await started.Task.WaitAsync(Limit);

        Assert.True(await DrainAsync("tenant-c"));

        release.SetResult();
        Assert.True(await DrainAsync("tenant-b"));
        Assert.Single(await TrackAsync("tenant-b"));
    }

    [Fact]
    public async Task Drain_MarkDuringRun_RunsAgain()
    {
        var first = true;
        _log.OnReadPositions = async tenant =>
        {
            if (!first) return;
            first = false;
            // An ingest lands mid-run, without Enqueue.
            await IngestAsync(null, tenant, 2000);
        };
        await IngestAsync(1000);

        Assert.True(await DrainAsync());

        Assert.Equal([T0 + 1000, T0 + 2000], (await TrackAsync()).Select(t => t.TimestampMs));
        Assert.Null((await DeviceProcessing.GetAsync(TenantA, Dev, CancellationToken.None))!.DirtyFromMs);
    }

    [Fact]
    public async Task Drain_RunThrows_ReturnsFalseOnceThenTrue()
    {
        _log.OnReadPositions = _ => throw new IOException("disk");
        await IngestAsync(1000);

        Assert.False(await DrainAsync());
        Assert.NotNull((await DeviceProcessing.GetAsync(TenantA, Dev, CancellationToken.None))!.DirtyFromMs);
        Assert.True(await DrainAsync());
    }

    [Fact]
    public async Task Enqueue_WhileRunning_RunsAgain()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = true;
        _log.OnReadPositions = tenant =>
        {
            if (!first) return Task.CompletedTask;
            first = false;
            started.TrySetResult();
            return release.Task;
        };
        await IngestAsync(1000);
        await started.Task.WaitAsync(Limit);

        await IngestAsync(2000);
        release.SetResult();

        Assert.True(await DrainAsync());
        Assert.Equal([T0 + 1000, T0 + 2000], (await TrackAsync()).Select(t => t.TimestampMs));
    }

    // Wraps the real log; OnReadPositions runs before every raw position read.
    private sealed class FakeLog(IAppendLog inner) : IAppendLog
    {
        public Func<string, Task>? OnReadPositions { get; set; }

        public Task AppendAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord =>
            inner.AppendAsync(tenantId, key, records, ct);

        public Task<int> AppendNewAsync<T>(string tenantId, string key, IReadOnlyList<T> records, CancellationToken ct) where T : class, ILogRecord =>
            inner.AppendNewAsync(tenantId, key, records, ct);

        public async Task<IReadOnlyList<T>> ReadRangeAsync<T>(string tenantId, string key, long fromMs, long toMs, CancellationToken ct) where T : class, ILogRecord
        {
            if (typeof(T) == typeof(Position) && OnReadPositions is { } hook) await hook(tenantId);
            return await inner.ReadRangeAsync<T>(tenantId, key, fromMs, toMs, ct);
        }
    }
}
