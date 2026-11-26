namespace SiteSense.Domain.Geometry;

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
