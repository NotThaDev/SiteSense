using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;
using SiteSense.Domain.Navigation;

namespace SiteSense.Tests.Api.Maps;

public sealed class RouteGraphMapTests
{
    private static readonly NavigationSettings Nav = new(1.3, 0.8, 0.5, 0.5, 30);

    private const string SyntheticMap = """
    {
      "type": "FeatureCollection",
      "features": [
        { "type": "Feature",
          "geometry": { "type": "Polygon", "coordinates": [[[15.06, 37.51], [15.08, 37.51], [15.08, 37.53], [15.06, 37.53], [15.06, 37.51]]] },
          "properties": { "id": "perimeter", "kind": "area", "name": "Site", "areaType": "perimeter" } },
        { "type": "Feature",
          "geometry": { "type": "Polygon", "coordinates": [[[15.0700, 37.5200], [15.0702, 37.5200], [15.0702, 37.5202], [15.0700, 37.5202], [15.0700, 37.5200]]] },
          "properties": { "id": "hz", "kind": "area", "name": "Pit", "areaType": "hazard" } },
        { "type": "Feature",
          "geometry": { "type": "Polygon", "coordinates": [[[15.0710, 37.5200], [15.0712, 37.5200], [15.0712, 37.5202], [15.0710, 37.5202], [15.0710, 37.5200]]] },
          "properties": { "id": "wk", "kind": "area", "name": "Yard", "areaType": "work", "capacity": 30 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.0690, 37.5201], [15.0698, 37.5201], [15.0704, 37.5201]] },
          "properties": { "id": "a", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.0704, 37.5201], [15.0714, 37.5201]] },
          "properties": { "id": "b", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.06900, 37.52150], [15.06950, 37.52150]] },
          "properties": { "id": "c", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.069503, 37.52150], [15.06990, 37.52150]] },
          "properties": { "id": "d", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.0710, 37.5190], [15.0710, 37.5195]] },
          "properties": { "id": "e", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [15.0690, 37.5201] },
          "properties": { "id": "ap", "kind": "assemblyPoint", "name": "AP", "capacity": 10 } },
        { "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [15.0710, 37.5190] },
          "properties": { "id": "en", "kind": "entrance", "name": "Gate" } }
      ]
    }
    """;

    [Fact]
    public void SharedWgs84Vertex_JoinsPaths()
    {
        var map = Load(SyntheticMap);
        var g = map.BuildRouteGraph(Nav);
        var shared = NodeAt(map, g, 15.0704, 37.5201);

        var pathIds = g.EdgesOf[shared].Select(i => g.Edges[i].PathId).ToList();
        Assert.Contains("a", pathIds);
        Assert.Contains("b", pathIds);
    }

    [Fact]
    public void CloseEndpoints_GetConnector()
    {
        var map = Load(SyntheticMap);
        var g = map.BuildRouteGraph(Nav);

        var connector = Assert.Single(g.Edges, e => e.PathId is null);
        Assert.InRange(connector.LengthM, 0.2, 0.35);
    }

    [Fact]
    public void HazardArea_BlocksEdge()
    {
        var map = Load(SyntheticMap);
        var g = map.BuildRouteGraph(Nav);
        var start = NodeAt(map, g, 15.0690, 37.5201);
        var end = NodeAt(map, g, 15.0704, 37.5201);

        var first = Assert.Single(g.Edges, e => e.PathId == "a" && e.From == start);
        var crossing = Assert.Single(g.Edges, e => e.PathId == "a" && (e.From == end || e.To == end));
        Assert.True(first.Traversable);
        Assert.False(crossing.Traversable);
    }

    [Fact]
    public void WorkArea_DoesNotBlock()
    {
        var map = Load(SyntheticMap);
        var g = map.BuildRouteGraph(Nav);

        Assert.All(g.Edges.Where(e => e.PathId == "e"), e => Assert.True(e.Traversable));
        // Path b crosses the work area, which is not blocked.
        Assert.All(g.Edges.Where(e => e.PathId == "b"), e => Assert.True(e.Traversable));
    }

    [Fact]
    public void SampleMap_IsOneConnectedComponent()
    {
        var map = Load(MapFixtures.RealMap);
        var g = map.BuildRouteGraph(Nav);

        var seen = new HashSet<int> { 0 };
        var queue = new Queue<int>([0]);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            foreach (var ei in g.EdgesOf[n])
            {
                var e = g.Edges[ei];
                var other = e.From == n ? e.To : e.From;
                if (seen.Add(other)) queue.Enqueue(other);
            }
        }

        Assert.Equal(g.Nodes.Count, seen.Count);
        Assert.NotEmpty(g.Edges);
    }

    private static SiteMap Load(string geoJson)
    {
        var (map, errors) = MapValidator.Validate(geoJson, 0.5);
        Assert.Empty(errors);
        return map!;
    }

    private static int NodeAt(SiteMap map, RouteGraph g, double lon, double lat)
    {
        var perimeter = map.Areas.Single(a => a.AreaType == "perimeter").Coordinates[0];
        var projection = LocalProjection.FromCentroid(perimeter.Select(p => (Lon: p[0], Lat: p[1])).ToList());
        var (x, y) = projection.ToLocal(lon, lat);
        return g.Nodes.Single(n => Math.Abs(n.X - x) < 0.01 && Math.Abs(n.Y - y) < 0.01).Id;
    }
}
