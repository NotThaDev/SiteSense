using SiteSense.Api.Models;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Services;

// Current area with hysteresis (SS-25, Q27). Pure: no I/O, so a replay gives the same areas as an in-order run,
// except when the replay starts inside a pending streak (the seed holds no streak).
public static class AreaAssigner
{
    public static IReadOnlyList<TrackPoint> Assign(string? seedAreaId, IReadOnlyList<TrackPoint> points, SiteAreas? areas, TrackingOptions o)
    {
        if (areas is null) return points;

        // An unknown seed id (or an old row without one) counts as no area.
        string? cur = seedAreaId is not null && areas.Knows(seedAreaId) ? seedAreaId : null;
        // The area the points have named since the last switch, and how many points in a row named it.
        string? pending = null;
        var count = 0;
        var output = new List<TrackPoint>(points.Count);
        foreach (var p in points)
        {
            // Tombstones keep their row and do not count toward the streak.
            if (p.Dropped)
            {
                output.Add(p);
                continue;
            }

            var candidate = areas.Candidate(p.Lon, p.Lat);
            if (cur is null)
            {
                cur = candidate;
            }
            else if (candidate == cur)
            {
                pending = null;
                count = 0;
            }
            else
            {
                // The accuracy widens the margin: a noisy fix must be clearly inside to switch.
                var m = Math.Max(p.AccuracyM, o.AreaMarginM);
                // A sub-area is left only when the point is outside it by more than m; the perimeter is never sticky.
                if (!areas.IsPerimeter(cur) && areas.Locate(cur, p.Lon, p.Lat, m) != PointLocation.Outside)
                {
                    pending = null;
                    count = 0;
                }
                else
                {
                    count = candidate == pending ? count + 1 : 1;
                    pending = candidate;
                    if (areas.Locate(candidate, p.Lon, p.Lat, m) == PointLocation.Inside || count >= o.AreaConfirmPoints)
                    {
                        cur = candidate;
                        pending = null;
                        count = 0;
                    }
                }
            }
            output.Add(p with { AreaId = cur });
        }
        return output;
    }
}
