using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Alarms;

// One alarm per entry of a device into a restricted or hazard area (EVT-02, SS-28). Stateless: the previous point comes from the track.
public static class AreaIntrusionDetector
{
    private const double BoundaryToleranceM = 0.5;
    private const long LookbackMs = 10 * 60_000;

    // Throws on failure so the processor keeps the dirty marker and retries (alarm ids are stable).
    // Runs after the run's track append: the lookback read stops before the first fresh point, so it never sees this run's points.
    public static async Task RunAsync(AlarmManager alarms, string tenantId, string deviceId, IReadOnlyList<Position> fresh, bool late, CancellationToken ct)
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
        (double X, double Y)? previous = before.Count > 0 ? projection.ToLocal(before[^1].Lon, before[^1].Lat) : null;

        foreach (var p in points)
        {
            var here = projection.ToLocal(p.Lon, p.Lat);
            foreach (var area in areas)
            {
                var wasIn = previous is { } q && IsIn(q, area);
                var isIn = IsIn(here, area);
                if (isIn && !wasIn)
                    await alarms.NotifyAlarmAsync(tenantId, device.SiteId, AlertTypes.AreaIntrusion, deviceId, area.Id, p.TimestampMs, late, ct);
                else if (wasIn && !isIn)
                    foreach (var alert in await Alert.ListActiveAsync(tenantId, device.SiteId, deviceId, area.Id, ct))
                        await alarms.DeactivateAlarmAsync(tenantId, alert.Id, p.TimestampMs, ct);
            }
            previous = here;
        }
    }

    private static bool IsIn((double X, double Y) p, ProjectedArea area) =>
        PlanarGeometry.Locate(p, area.Rings, BoundaryToleranceM) != PointLocation.Outside;
}
