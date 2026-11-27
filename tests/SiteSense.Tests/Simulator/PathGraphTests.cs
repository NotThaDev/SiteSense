using System.Collections.Generic;
using SiteSense.Simulator;

namespace SiteSense.Tests.Simulator;

public sealed class PathGraphTests
{
    // Square perimeter around lon 15.07, lat 37.52.
    private const string Perimeter = """
        {"type":"Feature","properties":{"kind":"area","areaType":"perimeter"},"geometry":{"type":"Polygon","coordinates":[[[15.06,37.51],[15.08,37.51],[15.08,37.53],[15.06,37.53],[15.06,37.51]]]}}
        """;

    private static string Path(string coordinates) =>
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"path\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":" + coordinates + "}}";

    private static string Map(params string[] paths) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", paths.Prepend(Perimeter)) + "]}";

    [Fact]
    public void Load_TwoPathsSharingVertex_OneConnectedComponent()
    {
        var graph = PathGraph.Load(Map(
            Path("[[15.070,37.520],[15.071,37.520]]"),
            Path("[[15.071,37.520],[15.071,37.521]]")));

        Assert.Equal(3, graph.Nodes.Count);

        var reached = new HashSet<int> { 0 };
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var next in graph.Neighbours(node))
                if (reached.Add(next)) queue.Enqueue(next);
        }
        Assert.Equal(graph.Nodes.Count, reached.Count);
    }

    [Fact]
    public void Load_VertexOutsidePerimeter_IsDropped()
    {
        var graph = PathGraph.Load(Map(
            Path("[[15.070,37.520],[15.071,37.520]]"),
            Path("[[15.071,37.520],[15.200,37.520]]")));

        Assert.Equal(2, graph.Nodes.Count);
        Assert.All(graph.Nodes, p => Assert.True(p.Lon < 15.1));
    }

    [Fact]
    public void Load_NoPathInsidePerimeter_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PathGraph.Load(Map(Path("[[15.200,37.520],[15.210,37.520]]"))));
    }
}
