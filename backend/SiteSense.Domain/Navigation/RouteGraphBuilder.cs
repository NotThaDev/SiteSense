using SiteSense.Domain.Geometry;

namespace SiteSense.Domain.Navigation;

public static class RouteGraphBuilder
{
    public static RouteGraph Build(IReadOnlyList<GraphPath> paths,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> blockedAreas, GraphSettings settings)
    {
        var nodes = new List<GraphNode>();
        var nodeIds = new Dictionary<(long, long), int>();
        var edges = new List<GraphEdge>();

        int NodeOf((double X, double Y) p)
        {
            var key = ((long)Math.Round(p.X / 0.01), (long)Math.Round(p.Y / 0.01));
            if (!nodeIds.TryGetValue(key, out var id))
            {
                id = nodes.Count;
                nodes.Add(new GraphNode(id, p.X, p.Y));
                nodeIds[key] = id;
            }
            return id;
        }

        var pathEnds = new List<(int Node, int Path)>();
        for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++)
        {
            var path = paths[pathIndex];
            var ids = path.Points.Select(p => NodeOf(p)).ToList();
            var factor = (path.Unpaved ? settings.UnpavedSpeedFactor : 1) * (path.Steps ? settings.StepsSpeedFactor : 1);
            for (var i = 0; i + 1 < ids.Count; i++)
            {
                var from = ids[i];
                var to = ids[i + 1];
                if (from == to) continue;
                var length = Distance(nodes[from], nodes[to]);
                edges.Add(new GraphEdge(from, to, length, length / (settings.WalkingSpeedMps * factor), true, path.Id));
            }
            if (ids.Count >= 2)
            {
                pathEnds.Add((ids[0], pathIndex));
                pathEnds.Add((ids[^1], pathIndex));
            }
        }

        // Connectors: an edge joins two end nodes of different paths closer than the tolerance (Q18).
        var joined = new HashSet<(int, int)>();
        foreach (var e in edges) joined.Add((Math.Min(e.From, e.To), Math.Max(e.From, e.To)));
        var ends = pathEnds.Distinct().ToList();
        // shortcut: all end pairs are compared, O(n²); add a grid index if maps reach thousands of paths.
        for (var i = 0; i < ends.Count; i++)
            for (var j = i + 1; j < ends.Count; j++)
            {
                var (nodeA, pathA) = ends[i];
                var (nodeB, pathB) = ends[j];
                if (pathA == pathB || nodeA == nodeB) continue;
                var key = (Math.Min(nodeA, nodeB), Math.Max(nodeA, nodeB));
                if (joined.Contains(key)) continue;
                var length = Distance(nodes[nodeA], nodes[nodeB]);
                if (length >= settings.ConnectToleranceM) continue;
                edges.Add(new GraphEdge(nodeA, nodeB, length, length / settings.WalkingSpeedMps, true, null));
                joined.Add(key);
            }

        for (var k = 0; k < edges.Count; k++)
        {
            var e = edges[k];
            var a = (nodes[e.From].X, nodes[e.From].Y);
            var b = (nodes[e.To].X, nodes[e.To].Y);
            var traversable = !blockedAreas.Any(rings => PlanarGeometry.SegmentCrossesInterior(a, b, rings));
            edges[k] = e with { Traversable = traversable };
        }

        var edgesOf = new List<int>[nodes.Count];
        for (var n = 0; n < nodes.Count; n++) edgesOf[n] = [];
        for (var k = 0; k < edges.Count; k++)
        {
            edgesOf[edges[k].From].Add(k);
            edgesOf[edges[k].To].Add(k);
        }

        return new RouteGraph(nodes, edges, edgesOf);
    }

    private static double Distance(GraphNode a, GraphNode b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
