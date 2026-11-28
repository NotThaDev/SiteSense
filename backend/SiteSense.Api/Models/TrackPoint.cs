using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

// Processed track (Q16, Q101, SS-24): the filtered raw positions. A Dropped row is a tombstone: it is never read as track.
public sealed record TrackPoint(string DeviceId, long TimestampMs, double Lat, double Lon, double AccuracyM, long Revision, bool Dropped = false) : ILogRecord
{
    public static async Task AppendAsync(string tenantId, string deviceId, IReadOnlyList<TrackPoint> points, CancellationToken ct)
    {
        if (points.Any(p => p.DeviceId != deviceId))
            throw new ArgumentException("Every point must belong to the device it is appended under.", nameof(points));
        await SiteSenseContext.Log.AppendAsync(tenantId, deviceId, points, ct);
    }

    // The track: tombstones left out. Empty for an id the store would reject.
    public static async Task<IReadOnlyList<TrackPoint>> ReadAsync(string tenantId, string deviceId, long fromMs, long toMs, CancellationToken ct) =>
        (await ReadWithDroppedAsync(tenantId, deviceId, fromMs, toMs, ct)).Where(p => !p.Dropped).ToList();

    // Same fold with the tombstones, so a caller can tell a decided timestamp from a new one.
    public static async Task<IReadOnlyList<TrackPoint>> ReadWithDroppedAsync(string tenantId, string deviceId, long fromMs, long toMs, CancellationToken ct)
    {
        if (!FileDocumentStore.IsValidName(deviceId)) return [];
        var raw = await SiteSenseContext.Log.ReadRangeAsync<TrackPoint>(tenantId, deviceId, fromMs, toMs, ct);
        // Replays append newer revisions (Q06): per timestamp the highest revision wins; repeats of one revision collapse.
        return raw.GroupBy(p => p.TimestampMs)
            .Select(g => g.Aggregate((best, p) => p.Revision >= best.Revision ? p : best))
            .OrderBy(p => p.TimestampMs)
            .ToList();
    }
}
