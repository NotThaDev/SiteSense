using System.Collections.Concurrent;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Realtime;

namespace SiteSense.Api.Services;

public sealed record AreaCount(string AreaId, int Count);

// People per area (SS-27, spec §7.3): a device counts where its last valid track point is, if that point is less than 5 minutes old.
// Computed from the track log on demand, never stored (Q46, Q120, Q121).
public sealed class OccupancyService(IRealtimePublisher publisher, ILogger<OccupancyService> logger)
{
    private const long WindowMs = 5 * 60_000;

    private readonly ConcurrentDictionary<(string Tenant, string Site), IReadOnlyList<AreaCount>> _published = new();
    private readonly ConcurrentDictionary<(string Tenant, string Site), SemaphoreSlim> _gates = new();

    // Every area of the map in map order, perimeter included, 0 when empty. No map: empty list.
    // shortcut: one log read per device per call; cache the last point per device if the benchmark (SS-13) shows it.
    public async Task<IReadOnlyList<AreaCount>> ComputeAsync(string tenantId, string siteId, long nowMs, CancellationToken ct)
    {
        var map = await SiteMap.GetAsync(tenantId, siteId, ct);
        if (map is null) return [];
        var counts = map.Areas.ToDictionary(a => a.Id, _ => 0);
        foreach (var device in await Device.GetAllAsync(tenantId, siteId, ct))
        {
            // From inclusive, to exclusive: ages 0 to 299999 ms. A point after nowMs is left out.
            var last = (await TrackPoint.ReadAsync(tenantId, device.Id, nowMs - WindowMs + 1, nowMs + 1, ct)).LastOrDefault();
            // An area missing from the map (replaced since) is not counted (Q120).
            if (last?.AreaId is { } areaId && counts.ContainsKey(areaId)) counts[areaId]++;
        }
        return map.Areas.Select(a => new AreaCount(a.Id, counts[a.Id])).ToList();
    }

    // Publishes OccupancyChanged only when the snapshot differs from the last one published for the site (the first call after a restart always publishes).
    // A failed publish is logged and not remembered, so the next call retries. No device ids or coordinates in the log (PRV).
    public async Task PublishIfChangedAsync(string tenantId, string siteId, CancellationToken ct)
    {
        var key = (tenantId, siteId);
        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var nowMs = SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds();
            var areas = await ComputeAsync(tenantId, siteId, nowMs, ct);
            if (_published.TryGetValue(key, out var last) && last.SequenceEqual(areas)) return;
            try
            {
                await publisher.PublishAsync(tenantId, siteId, EventTypes.OccupancyChanged, new { areas }, ct);
                _published[key] = areas;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "OccupancyChanged not published for site {SiteId} in tenant {TenantId}", siteId, tenantId);
            }
        }
        finally
        {
            gate.Release();
        }
    }
}
