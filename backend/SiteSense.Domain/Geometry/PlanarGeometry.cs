namespace SiteSense.Domain.Geometry;

public enum PointLocation { Outside, OnBoundary, Inside }

public static class PlanarGeometry
{
    /// <summary>Shortest distance from p to the polyline, in the same unit as the coordinates.</summary>
    public static double DistanceToPolyline((double X, double Y) p, IReadOnlyList<(double X, double Y)> line)
    {
        if (line.Count == 0) return double.PositiveInfinity;
        if (line.Count == 1) return Distance(p, line[0]);

        var best = double.PositiveInfinity;
        for (var i = 0; i < line.Count - 1; i++)
            best = Math.Min(best, DistanceToSegment(p, line[i], line[i + 1]));
        return best;
    }

    /// <summary>Where p lies against a polygon: ring 0 is the outer ring, the others are holes (even-odd rule).
    /// A point within boundaryTolerance of any ring edge is OnBoundary; each caller decides what that means.</summary>
    public static PointLocation Locate((double X, double Y) p, IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings, double boundaryTolerance)
    {
        foreach (var ring in rings)
            foreach (var (a, b) in Edges(ring))
                if (DistanceToSegment(p, a, b) <= boundaryTolerance) return PointLocation.OnBoundary;

        var inside = false;
        foreach (var ring in rings)
            foreach (var (a, b) in Edges(ring))
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;

        return inside ? PointLocation.Inside : PointLocation.Outside;
    }

    /// <summary>True if segment a→b passes through the inside of the polygon (rings: outer first, then holes). Touching or running along the boundary is not inside (Q25).</summary>
    public static bool SegmentCrossesInterior((double X, double Y) a, (double X, double Y) b, IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings)
    {
        const double Eps = 1e-6;
        if (rings.Count == 0 || rings[0].Count == 0) return false;
        if (a == b) return Locate(a, rings, Eps) == PointLocation.Inside;

        var outer = rings[0];
        if (Math.Max(a.X, b.X) < outer.Min(p => p.X) - Eps || Math.Min(a.X, b.X) > outer.Max(p => p.X) + Eps ||
            Math.Max(a.Y, b.Y) < outer.Min(p => p.Y) - Eps || Math.Min(a.Y, b.Y) > outer.Max(p => p.Y) + Eps)
            return false;

        var r = (X: b.X - a.X, Y: b.Y - a.Y);
        var rLength = Math.Sqrt(r.X * r.X + r.Y * r.Y);
        var ts = new List<double> { 0, 1 };
        foreach (var ring in rings)
            foreach (var (c, d) in Edges(ring))
            {
                var s = (X: d.X - c.X, Y: d.Y - c.Y);
                var ca = (X: c.X - a.X, Y: c.Y - a.Y);
                var den = Cross(r, s);
                if (Math.Abs(den) > 1e-12)
                {
                    var t = Cross(ca, s) / den;
                    var u = Cross(ca, r) / den;
                    if (t >= -1e-12 && t <= 1 + 1e-12 && u >= -1e-12 && u <= 1 + 1e-12)
                        ts.Add(Math.Clamp(t, 0, 1));
                }
                else if (Math.Abs(Cross(ca, r)) / rLength < Eps)
                {
                    foreach (var p in new[] { c, d })
                    {
                        var t = ((p.X - a.X) * r.X + (p.Y - a.Y) * r.Y) / (rLength * rLength);
                        if (t >= 0 && t <= 1) ts.Add(t);
                    }
                }
            }

        ts.Sort();
        for (var k = 0; k + 1 < ts.Count; k++)
        {
            if (ts[k + 1] - ts[k] <= 1e-12) continue;
            var mid = (ts[k] + ts[k + 1]) / 2;
            var m = (X: a.X + r.X * mid, Y: a.Y + r.Y * mid);
            if (Locate(m, rings, Eps) == PointLocation.Inside) return true;
        }
        return false;
    }

    private static double Cross((double X, double Y) u, (double X, double Y) v) => u.X * v.Y - u.Y * v.X;

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Edges(IReadOnlyList<(double X, double Y)> ring)
    {
        if (ring.Count < 2) yield break;

        for (var i = 0; i < ring.Count - 1; i++)
            yield return (ring[i], ring[i + 1]);
        if (ring[^1] != ring[0])
            yield return (ring[^1], ring[0]);
    }

    private static double DistanceToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSquared = dx * dx + dy * dy;
        if (lengthSquared == 0) return Distance(p, a);

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSquared, 0, 1);
        return Distance(p, (a.X + t * dx, a.Y + t * dy));
    }

    private static double Distance((double X, double Y) p, (double X, double Y) q) =>
        Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
}
