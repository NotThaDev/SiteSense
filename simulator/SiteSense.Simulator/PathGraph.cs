using System.Text.Json;

namespace SiteSense.Simulator;

public sealed class PathGraph
{
    private readonly GeoPoint[] _nodes;
    private readonly int[][] _neighbours;
    private readonly GeoPoint[] _perimeter;

    private PathGraph(GeoPoint[] nodes, int[][] neighbours, GeoPoint[] perimeter)
    {
        _nodes = nodes;
        _neighbours = neighbours;
        _perimeter = perimeter;
        // Local east-west scale at the map's mean latitude: the simulator has no projection library.
        var meanLat = nodes.Average(p => p.Lat);
        MetresPerDegLon = 111_320.0 * Math.Cos(meanLat * Math.PI / 180.0);
    }

    public IReadOnlyList<GeoPoint> Nodes => _nodes;

    public IReadOnlyList<GeoPoint> Perimeter => _perimeter;

    public double MetresPerDegLat => 111_320.0;

    public double MetresPerDegLon { get; }

    public IReadOnlyList<int> Neighbours(int node) => _neighbours[node];

    public double DistanceM(GeoPoint a, GeoPoint b)
    {
        var dx = (b.Lon - a.Lon) * MetresPerDegLon;
        var dy = (b.Lat - a.Lat) * MetresPerDegLat;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static bool Inside(IReadOnlyList<GeoPoint> ring, GeoPoint p)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Lat > p.Lat) != (b.Lat > p.Lat) &&
                p.Lon < (b.Lon - a.Lon) * (p.Lat - a.Lat) / (b.Lat - a.Lat) + a.Lon)
                inside = !inside;
        }
        return inside;
    }

    public static PathGraph Load(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var perimeter = Array.Empty<GeoPoint>();
        var paths = new List<GeoPoint[]>();
        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var properties = feature.GetProperty("properties");
            var geometry = feature.GetProperty("geometry");
            var kind = Prop(properties, "kind");
            var type = geometry.GetProperty("type").GetString();

            if (kind == "area" && type == "Polygon" && Prop(properties, "areaType") == "perimeter")
                perimeter = ReadRing(geometry.GetProperty("coordinates")[0]);
            else if (kind == "path" && type == "LineString")
                paths.Add(ReadRing(geometry.GetProperty("coordinates")));
        }

        // Vertices with the same rounded key are one node, so shared endpoints join paths.
        var index = new Dictionary<(double Lon, double Lat), int>();
        var points = new List<GeoPoint>();
        int? NodeOf(GeoPoint p)
        {
            if (perimeter.Length > 0 && !Inside(perimeter, p)) return null;
            var key = (Math.Round(p.Lon, 7), Math.Round(p.Lat, 7));
            if (!index.TryGetValue(key, out var id))
            {
                id = points.Count;
                index[key] = id;
                points.Add(new GeoPoint(key.Item1, key.Item2));
            }
            return id;
        }

        var edges = new HashSet<(int, int)>();
        foreach (var path in paths)
            for (var i = 0; i + 1 < path.Length; i++)
            {
                if (NodeOf(path[i]) is not int a || NodeOf(path[i + 1]) is not int b || a == b) continue;
                edges.Add((Math.Min(a, b), Math.Max(a, b)));
            }

        // Drop vertices that touch no edge, then renumber the rest.
        var degree = new int[points.Count];
        foreach (var (a, b) in edges)
        {
            degree[a]++;
            degree[b]++;
        }
        var remap = new int[points.Count];
        var nodes = new List<GeoPoint>();
        for (var i = 0; i < points.Count; i++)
        {
            remap[i] = degree[i] > 0 ? nodes.Count : -1;
            if (degree[i] > 0) nodes.Add(points[i]);
        }
        if (nodes.Count == 0)
            throw new InvalidOperationException("The map has no path inside the perimeter.");

        var adjacency = nodes.Select(_ => new List<int>()).ToArray();
        foreach (var (a, b) in edges)
        {
            adjacency[remap[a]].Add(remap[b]);
            adjacency[remap[b]].Add(remap[a]);
        }
        return new PathGraph(nodes.ToArray(), adjacency.Select(l => l.ToArray()).ToArray(), perimeter);
    }

    private static string? Prop(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static GeoPoint[] ReadRing(JsonElement coordinates) =>
        coordinates.EnumerateArray().Select(c => new GeoPoint(c[0].GetDouble(), c[1].GetDouble())).ToArray();
}
