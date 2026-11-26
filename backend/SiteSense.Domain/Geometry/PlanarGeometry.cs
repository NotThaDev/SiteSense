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
