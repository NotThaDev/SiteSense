using SiteSense.Api.Alarms;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.BackgroundJobs;

// DeviceSilent (TRK-06, SS-30): one alarm per silence episode, ended when the device transmits again.
// The alarm time is the device's last-seen time, so its id is the same on every run and retry (JOB-02).
public sealed class SilentDevicesJob(AlarmManager alarms) : ISiteJob
{
    public const string Name = "silent-devices";
    // A last valid position this close to an entrance is taken as an exit: out-of-perimeter positions are discarded, so the exit itself is never seen (Q29).
    public const double EntranceRadiusM = 15;
    // Looks back this far from the newest record for the newest valid point (the newest record may be a tombstone).
    private const long LastPointLookbackMs = 3_600_000;

    public async Task RunAsync(string tenantId, string siteId, long slotMs, CancellationToken ct)
    {
        if (!SiteSenseContext.Tenants.TryGetValue(tenantId, out var tenant)) return;
        var nowMs = SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds();
        var silentAfterMs = tenant.SilentAfterMinutes * 60_000L;
        var devices = await Device.GetAllAsync(tenantId, siteId, ct);
        if (devices.Count == 0) return;

        var map = await SiteMap.GetAsync(tenantId, siteId, ct);
        var entrances = map is null || map.Entrances.Count == 0 ? ((LocalProjection, List<(double, double)>)?)null : ProjectEntrances(map);
        // One read per run: the Active DeviceSilent alerts of the site, to end those of devices that transmit again.
        var active = (await Alert.ListAsync(tenantId, siteId, ct))
            .Where(a => a.Type == AlertTypes.DeviceSilent && a.State == AlertStates.Active)
            .ToLookup(a => a.DeviceId);

        foreach (var device in devices)
        {
            var last = await LastPointAsync(tenantId, device.Id, ct);
            if (last is null) continue;

            if (nowMs - last.TimestampMs < silentAfterMs)
            {
                foreach (var alert in active[device.Id].Where(a => a.EventTimeMs < last.TimestampMs))
                    await alarms.DeactivateAlarmAsync(tenantId, alert.Id, last.TimestampMs, ct);
                continue;
            }
            if (entrances is not null && IsNearEntrance(last, entrances.Value)) continue;
            await alarms.NotifyAlarmAsync(tenantId, siteId, AlertTypes.DeviceSilent, device.Id, last.AreaId, last.TimestampMs, replay: false, ct);
        }
    }

    private static async Task<TrackPoint?> LastPointAsync(string tenantId, string deviceId, CancellationToken ct)
    {
        var newest = await SiteSenseContext.Log.ReadLastAsync<TrackPoint>(tenantId, deviceId, ct);
        if (newest is null) return null;
        var valid = await TrackPoint.ReadAsync(tenantId, deviceId, newest.TimestampMs - LastPointLookbackMs, newest.TimestampMs + 1, ct);
        return valid.LastOrDefault();
    }

    private static (LocalProjection Projection, List<(double X, double Y)> Points) ProjectEntrances(SiteMap map)
    {
        var projection = map.ProjectBlockedAreas().Projection;
        return (projection, map.Entrances.Select(e => projection.ToLocal(e.Coordinates[0], e.Coordinates[1])).ToList());
    }

    private static bool IsNearEntrance(TrackPoint p, (LocalProjection Projection, List<(double X, double Y)> Points) entrances)
    {
        var (x, y) = entrances.Projection.ToLocal(p.Lon, p.Lat);
        return entrances.Points.Any(e => double.Hypot(e.X - x, e.Y - y) <= EntranceRadiusM);
    }
}
