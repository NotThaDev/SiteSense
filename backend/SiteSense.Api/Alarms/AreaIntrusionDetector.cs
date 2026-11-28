using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Alarms;

// One alarm per stay of a device in a restricted or hazard area (EVT-02, SS-28). Stateless: the earlier points come from the track.
public static class AreaIntrusionDetector
{
    private const double BoundaryToleranceM = 0.5;
    private const long LookbackMs = 10 * 60_000;
    // A change of side counts only after 30 s of samples on the new side (GPS jitter at an edge).
    private const long DwellMs = 30_000;

    // Throws on failure so the processor keeps the dirty marker and retries (alarm ids are stable).
    // Runs after the run's track append: the lookback read stops before the first fresh point, so it never sees this run's points.
    public static async Task RunAsync(AlarmManager alarms, string tenantId, string deviceId, IReadOnlyList<TrackPoint> fresh, bool late, CancellationToken ct)
    {
        if (fresh.Count == 0) return;
        var device = await Device.GetAsync(tenantId, deviceId, ct);
        if (device is null) return;
        var map = await SiteMap.GetAsync(tenantId, device.SiteId, ct);
        if (map is null) return;
        var (projection, areas) = map.ProjectBlockedAreas();
        if (areas.Count == 0) return;

        var points = fresh.OrderBy(p => p.TimestampMs).ToList();
        var first = points[0].TimestampMs;
        var before = await TrackPoint.ReadAsync(tenantId, deviceId, first - LookbackMs, first, ct);
        // Lookback points only rebuild the state; a flip they cause was already handled when they were fresh.
        var walk = before.Select(t => (t.TimestampMs, t.Lon, t.Lat, IsFresh: false))
            .Concat(points.Select(p => (p.TimestampMs, p.Lon, p.Lat, IsFresh: true)));

        var states = areas.ToDictionary(a => a.Id, _ => (Inside: false, Since: (long?)null));
        foreach (var (ts, lon, lat, isFresh) in walk)
        {
            var here = projection.ToLocal(lon, lat);
            foreach (var area in areas)
            {
                var (inside, since) = states[area.Id];
                if (IsIn(here, area) == inside)
                {
                    states[area.Id] = (inside, null);
                    continue;
                }
                since ??= ts;
                if (ts - since < DwellMs)
                {
                    states[area.Id] = (inside, since);
                    continue;
                }
                states[area.Id] = (!inside, null);
                if (!isFresh) continue;
                if (!inside)
                    await alarms.NotifyAlarmAsync(tenantId, device.SiteId, AlertTypes.AreaIntrusion, deviceId, area.Id, since.Value, late, ct);
                else
                    foreach (var alert in await Alert.ListActiveAsync(tenantId, device.SiteId, deviceId, area.Id, ct))
                        await alarms.DeactivateAlarmAsync(tenantId, alert.Id, since.Value, ct);
            }
        }
    }

    private static bool IsIn((double X, double Y) p, ProjectedArea area) =>
        PlanarGeometry.Locate(p, area.Rings, BoundaryToleranceM) != PointLocation.Outside;
}
