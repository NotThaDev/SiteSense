using System.Globalization;

namespace SiteSense.Domain.Navigation;

public enum RouteStatus { Ok, StartTooFar, Unreachable }

public sealed record RouteResult(
    RouteStatus Status, IReadOnlyList<(double X, double Y)> Points, double DistanceM, double DurationS, string? Detail);

/// <summary>Fastest pedestrian route on a <see cref="RouteGraph"/> (A*), in local metres.</summary>
public static class RouteFinder
{
    private sealed record Snap(int Edge, double T, (double X, double Y) Point, double DistanceM);

    public static RouteResult Find(
        RouteGraph graph, (double X, double Y) from, (double X, double Y) to, double maxSnapDistanceM, double walkingSpeedMps)
    {
        var s = SnapTo(graph, from);
        if (s is null || s.DistanceM > maxSnapDistanceM)
            return Fail(RouteStatus.StartTooFar, "start", s, maxSnapDistanceM);
        var d = SnapTo(graph, to);
        if (d is null || d.DistanceM > maxSnapDistanceM)
            return Fail(RouteStatus.Unreachable, "destination", d, maxSnapDistanceM);

        var n = graph.Nodes.Count;
        int start = n, goal = n + 1; // virtual nodes for the two snap points
        var es = graph.Edges[s.Edge];
        var ed = graph.Edges[d.Edge];

        (double X, double Y) Pos(int i) => i == start ? s.Point : i == goal ? d.Point : (graph.Nodes[i].X, graph.Nodes[i].Y);

        // Admissible: every edge speed is <= walkingSpeedMps (all factors <= 1).
        double H(int i) => Dist(Pos(i), d.Point) / walkingSpeedMps;

        IEnumerable<(int To, double Time, double Len)> Links(int i)
        {
            if (i == start)
            {
                yield return (es.From, es.TimeS * s.T, es.LengthM * s.T);
                yield return (es.To, es.TimeS * (1 - s.T), es.LengthM * (1 - s.T));
                if (s.Edge == d.Edge)
                    yield return (goal, es.TimeS * Math.Abs(s.T - d.T), es.LengthM * Math.Abs(s.T - d.T));
                yield break;
            }
            foreach (var ei in graph.EdgesOf[i])
            {
                var e = graph.Edges[ei];
                if (e.Traversable) yield return (e.From == i ? e.To : e.From, e.TimeS, e.LengthM);
            }
            if (i == ed.From) yield return (goal, ed.TimeS * d.T, ed.LengthM * d.T);
            if (i == ed.To) yield return (goal, ed.TimeS * (1 - d.T), ed.LengthM * (1 - d.T));
        }

        var g = Enumerable.Repeat(double.PositiveInfinity, n + 2).ToArray();
        var prev = new int[n + 2];
        var prevLen = new double[n + 2];
        var closed = new bool[n + 2];
        var open = new PriorityQueue<int, double>();
        g[start] = 0;
        open.Enqueue(start, H(start));
        while (open.TryDequeue(out var cur, out _))
        {
            if (closed[cur]) continue;
            closed[cur] = true;
            if (cur == goal) break;
            foreach (var (next, time, len) in Links(cur))
            {
                if (g[cur] + time >= g[next]) continue;
                g[next] = g[cur] + time;
                prev[next] = cur;
                prevLen[next] = len;
                open.Enqueue(next, g[next] + H(next));
            }
        }

        if (double.IsPositiveInfinity(g[goal]))
            return new(RouteStatus.Unreachable, [], 0, 0, "No walkable route between the start and the destination.");

        var chain = new List<int>();
        var onPathM = 0.0;
        for (var i = goal; i != start; i = prev[i]) { chain.Add(i); onPathM += prevLen[i]; }
        chain.Add(start);
        chain.Reverse();

        var raw = new List<(double X, double Y)> { from };
        raw.AddRange(chain.Select(Pos));
        raw.Add(to);
        var points = new List<(double X, double Y)>();
        foreach (var p in raw)
            if (points.Count == 0 || Dist(points[^1], p) >= 1e-9) points.Add(p);

        var legsM = Dist(from, s.Point) + Dist(d.Point, to);
        return new(RouteStatus.Ok, points, onPathM + legsM, g[goal] + legsM / walkingSpeedMps, null);
    }

    private static RouteResult Fail(RouteStatus status, string what, Snap? snap, double max) => new(
        status, [], 0, 0,
        snap is null
            ? $"No walkable path exists near the {what}."
            : string.Create(CultureInfo.InvariantCulture,
                $"The {what} is {snap.DistanceM:0.0} m from the nearest walkable path (max {max:0.#} m)."));

    private static Snap? SnapTo(RouteGraph graph, (double X, double Y) p)
    {
        Snap? best = null;
        for (var i = 0; i < graph.Edges.Count; i++)
        {
            var e = graph.Edges[i];
            if (!e.Traversable) continue;
            var a = graph.Nodes[e.From];
            var b = graph.Nodes[e.To];
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            var t = len2 == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            var q = (a.X + t * dx, a.Y + t * dy);
            var dist = Dist(p, q);
            if (best is null || dist < best.DistanceM) best = new Snap(i, t, q, dist);
        }
        return best;
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
