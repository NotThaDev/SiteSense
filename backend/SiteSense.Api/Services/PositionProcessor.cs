using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Realtime;
using SiteSense.Api.Store;

namespace SiteSense.Api.Services;

// Ordered processing (TRK-02, Q06, Q100): one worker, so each device runs alone and in timestamp order. Drain is per tenant (Q102).
// Reads SiteSenseContext on every call, never in a field (Q38): test hosts replace it.
public sealed class PositionProcessor(IRealtimePublisher publisher, ILogger<PositionProcessor> logger, IOptions<TrackingOptions> tracking) : BackgroundService
{
    // A device missing from _devices is idle.
    private enum RunState { Queued, Running, RunningDirty }

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Tenant, string Device), RunState> _devices = [];
    private readonly Dictionary<string, int> _pending = [];
    private readonly Dictionary<string, int> _failed = [];
    private readonly Dictionary<string, TaskCompletionSource> _idle = [];
    private readonly Channel<(string Tenant, string Device)> _queue =
        Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(string tenantId, string deviceId)
    {
        var key = (tenantId, deviceId);
        lock (_lock)
        {
            if (_devices.TryGetValue(key, out var state))
            {
                // Already queued: that run will see the new marker. Running: run once more after it.
                if (state == RunState.Running) _devices[key] = RunState.RunningDirty;
                return;
            }
            _devices[key] = RunState.Queued;
            _pending[tenantId] = _pending.GetValueOrDefault(tenantId) + 1;
        }
        _queue.Writer.TryWrite(key);
    }

    // True when every position of the tenant received so far is processed; false when a run failed since the last drain (reset here).
    // shortcut: waits for zero pending, so a constant load can delay it; count a per-tenant sequence if that matters.
    public async Task<bool> DrainAsync(string tenantId, CancellationToken ct)
    {
        Task wait;
        lock (_lock)
        {
            if (_pending.GetValueOrDefault(tenantId) == 0) wait = Task.CompletedTask;
            else
            {
                if (!_idle.TryGetValue(tenantId, out var tcs))
                    _idle[tenantId] = tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                wait = tcs.Task;
            }
        }
        await wait.WaitAsync(ct);
        lock (_lock) return !_failed.Remove(tenantId);
    }

    // Awaited before the server starts: devices left dirty by a crash or restart are queued first (Q100).
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var tenantId in SiteSenseContext.Tenants.Keys)
            foreach (var doc in await DeviceProcessing.ListAsync(tenantId, cancellationToken))
                if (doc.DirtyFromMs is not null) Enqueue(tenantId, doc.Id);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var key in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            lock (_lock) _devices[key] = RunState.Running;
            while (true)
            {
                var ok = true;
                try
                {
                    await RunAsync(key.Tenant, key.Device, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The marker stays set: the next batch or a restart retries. No coordinates in the log (PRV).
                    ok = false;
                    logger.LogError(ex, "Processing failed for device {DeviceId} in tenant {TenantId}", key.Device, key.Tenant);
                }
                var again = false;
                lock (_lock)
                {
                    if (ok && _devices[key] == RunState.RunningDirty)
                    {
                        _devices[key] = RunState.Running;
                        again = true;
                    }
                    else
                    {
                        _devices.Remove(key);
                        if (!ok) _failed[key.Tenant] = _failed.GetValueOrDefault(key.Tenant) + 1;
                        if (--_pending[key.Tenant] == 0)
                        {
                            _pending.Remove(key.Tenant);
                            if (_idle.Remove(key.Tenant, out var tcs)) tcs.SetResult();
                        }
                    }
                }
                if (!again) break;
            }
        }
    }

    // Processes raw positions from DirtyFromMs in timestamp order. A start at or before the cursor is late: new revision, replay (Q06).
    // A version conflict means a mark landed during the run: reload and run again.
    // After a successful save, publishes PositionUpdated once per position not processed before (SS-50, Q115).
    private async Task RunAsync(string tenantId, string deviceId, CancellationToken ct)
    {
        while (true)
        {
            var doc = await DeviceProcessing.GetAsync(tenantId, deviceId, ct);
            if (doc?.DirtyFromMs is not { } from) return;
            var late = doc.CursorMs is { } cursor && from <= cursor;
            var revision = late ? doc.Revision + 1 : doc.Revision;
            // Upper bound is exclusive; long.MaxValue also covers timestamps ahead of the clock (Q39).
            var raw = await Position.ReadAsync(tenantId, deviceId, from, long.MaxValue, ct);
            var fresh = await NewPositionsAsync(tenantId, deviceId, doc, late, from, raw, ct);
            var decided = new List<TrackPoint>();
            if (raw.Count > 0)
            {
                // Seed: the last kept point before this run, so the filter continues the track (SS-24, Q26).
                var seed = (await TrackPoint.ReadAsync(tenantId, deviceId, 0, from, ct)).LastOrDefault();
                decided = TrackFilter.Apply(seed, raw, revision, tracking.Value).ToList();
                await TrackPoint.AppendAsync(tenantId, deviceId, decided, ct);
                doc.CursorMs = Math.Max(doc.CursorMs ?? long.MinValue, raw[^1].TimestampMs);
            }
            doc.Revision = revision;
            doc.DirtyFromMs = null;
            try
            {
                await DeviceProcessing.SaveAsync(tenantId, doc, ct);
                // Only kept points of positions not published before; a dropped one is never published (SS-24).
                var freshMs = fresh.Select(p => p.TimestampMs).ToHashSet();
                await PublishAsync(tenantId, deviceId, decided.Where(t => !t.Dropped && freshMs.Contains(t.TimestampMs)).ToList(), ct);
                return;
            }
            catch (ConcurrencyException)
            {
                // A mark landed during the run: reload and run again.
            }
        }
    }

    // From the saved state only (cursor, revision), so a retry after a failed save computes the same set.
    // Raw log records, tombstones included, not TrackPoint.ReadAsync: its fold would hide points written by a failed attempt
    // and a fold of all revisions would hide a point published under an older revision.
    private static async Task<IReadOnlyList<Position>> NewPositionsAsync(string tenantId, string deviceId, DeviceProcessing saved, bool late, long from, IReadOnlyList<Position> raw, CancellationToken ct)
    {
        if (saved.CursorMs is not { } cursor) return raw;
        HashSet<long> known = [];
        if (late)
        {
            var old = await SiteSenseContext.Log.ReadRangeAsync<TrackPoint>(tenantId, deviceId, from, cursor + 1, ct);
            known = old.Where(t => t.Revision <= saved.Revision).Select(t => t.TimestampMs).ToHashSet();
        }
        return raw.Where(p => p.TimestampMs > cursor || !known.Contains(p.TimestampMs)).ToList();
    }

    // At most once: a crash before this loses the events (KNOWN_ISSUES). A failure is logged and skipped, never fails the run.
    private async Task PublishAsync(string tenantId, string deviceId, IReadOnlyList<TrackPoint> kept, CancellationToken ct)
    {
        if (kept.Count == 0) return;
        var device = await Device.GetAsync(tenantId, deviceId, ct);
        if (device is null) return;
        foreach (var p in kept)
        {
            try
            {
                await publisher.PublishAsync(tenantId, device.SiteId, EventTypes.PositionUpdated,
                    new PositionUpdatedData(deviceId, p.TimestampMs, p.Lat, p.Lon, null), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // No coordinates in the log (PRV).
                logger.LogWarning(ex, "PositionUpdated not published for device {DeviceId} at {TimestampMs}", deviceId, p.TimestampMs);
            }
        }
    }
}
