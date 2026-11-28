using SiteSense.Api.Models;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Services;

// Plausibility and noise filter (SS-24, Q26). Pure: no I/O, so a replay gives the same track as an in-order run,
// except when the replay starts inside a streak of drops (the streak then restarts from the seed).
public static class TrackFilter
{
    private const double MinAccuracyM = 1;

    // Returns one row per decided timestamp: kept points, and Dropped tombstones. Raw positions are read in timestamp order.
    public static IReadOnlyList<TrackPoint> Apply(TrackPoint? seed, IReadOnlyList<Position> raw, long revision, TrackingOptions o)
    {
        var output = new List<TrackPoint>(raw.Count);
        TrackPoint? kept = seed;
        long? last = seed?.TimestampMs;
        // The last dropped position while a streak of drops lasts, and the streak length.
        Position? pending = null;
        var streak = 0;
        foreach (var r in raw.OrderBy(p => p.TimestampMs))
        {
            var p = r with { AccuracyM = Clamp(r.AccuracyM) };
            // Same timestamp as a point already decided: the first one wins.
            if (p.TimestampMs == last) continue;
            last = p.TimestampMs;

            if (kept is null || !Jumps(kept.Lon, kept.Lat, Clamp(kept.AccuracyM), kept.TimestampMs, p, o))
            {
                kept = Keep(kept, p, revision);
                output.Add(kept);
                pending = null;
                streak = 0;
                continue;
            }

            // A drop that agrees with the previous drop extends the streak; otherwise a new streak starts.
            var agrees = pending is not null && !Jumps(pending.Lon, pending.Lat, pending.AccuracyM, pending.TimestampMs, p, o);
            streak = agrees ? streak + 1 : 1;
            pending = p;
            if (streak >= o.ResetAfter)
            {
                // The track follows the device again: the last of the streak is kept as is and replaces its tombstone.
                kept = new TrackPoint(p.DeviceId, p.TimestampMs, p.Lat, p.Lon, p.AccuracyM, revision);
                output.Add(kept);
                pending = null;
                streak = 0;
            }
            else
            {
                output.Add(new TrackPoint(p.DeviceId, p.TimestampMs, p.Lat, p.Lon, p.AccuracyM, revision, Dropped: true));
            }
        }
        return output;
    }

    // Weighted average of k and p, weights 1 / accuracy². Written as k + (p - k) * share, so equal points stay exact.
    // With no k (first point) p is kept as is.
    private static TrackPoint Keep(TrackPoint? k, Position p, long revision)
    {
        if (k is null) return new TrackPoint(p.DeviceId, p.TimestampMs, p.Lat, p.Lon, p.AccuracyM, revision);
        var weightK = 1 / Square(Clamp(k.AccuracyM));
        var weightP = 1 / Square(p.AccuracyM);
        var share = weightP / (weightK + weightP);
        return new TrackPoint(p.DeviceId, p.TimestampMs,
            k.Lat + (p.Lat - k.Lat) * share, k.Lon + (p.Lon - k.Lon) * share, p.AccuracyM, revision);
    }

    // True when p is farther from (lon, lat) than both accuracy radii plus what VMaxMps allows in the time gap.
    private static bool Jumps(double lon, double lat, double accuracyM, long fromMs, Position p, TrackingOptions o)
    {
        var (x, y) = new LocalProjection(lon, lat).ToLocal(p.Lon, p.Lat);
        var distanceM = Math.Sqrt(x * x + y * y);
        var seconds = Math.Max(p.TimestampMs - fromMs, 0) / 1000.0;
        return distanceM - (accuracyM + p.AccuracyM) > o.VMaxMps * seconds;
    }

    private static double Clamp(double accuracyM) => Math.Max(accuracyM, MinAccuracyM);

    private static double Square(double v) => v * v;
}
