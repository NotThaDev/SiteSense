using System.Collections.Generic;
using System.IO;
using SiteSense.Simulator;

namespace SiteSense.Tests.Simulator;

public sealed class SimulatedDeviceTests
{
    private const string Perimeter = """
        {"type":"Feature","properties":{"kind":"area","areaType":"perimeter"},"geometry":{"type":"Polygon","coordinates":[[[15.06,37.51],[15.08,37.51],[15.08,37.53],[15.06,37.53],[15.06,37.51]]]}}
        """;

    private static string PathFeature(string coordinates) =>
        "{\"type\":\"Feature\",\"properties\":{\"kind\":\"path\"},\"geometry\":{\"type\":\"LineString\",\"coordinates\":" + coordinates + "}}";

    private static string Map(params string[] paths) =>
        "{\"type\":\"FeatureCollection\",\"features\":[" + string.Join(",", paths.Prepend(Perimeter)) + "]}";

    // Metric distance from p to the segment a-b, in the graph's local scale.
    private static double DistanceToSegmentM(PathGraph graph, GeoPoint p, GeoPoint a, GeoPoint b)
    {
        var bx = (b.Lon - a.Lon) * graph.MetresPerDegLon;
        var by = (b.Lat - a.Lat) * graph.MetresPerDegLat;
        var px = (p.Lon - a.Lon) * graph.MetresPerDegLon;
        var py = (p.Lat - a.Lat) * graph.MetresPerDegLat;
        var len2 = bx * bx + by * by;
        var t = len2 == 0 ? 0 : Math.Clamp((px * bx + py * by) / len2, 0, 1);
        var dx = px - t * bx;
        var dy = py - t * by;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static PathGraph TwoSegments() =>
        PathGraph.Load(Map(PathFeature("[[15.070,37.520],[15.075,37.520],[15.075,37.523]]")));

    private static PathGraph Straight() =>
        PathGraph.Load(Map(PathFeature("[[15.070,37.520],[15.075,37.520]]")));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SiteSense.slnx not found above the test output.");
    }

    [Fact]
    public void Step_SameSeed_SameFixes()
    {
        var graph = TwoSegments();
        var a = new SimulatedDevice("d1", graph, 7);
        var b = new SimulatedDevice("d1", graph, 7);

        for (var i = 0; i < 100; i++)
            Assert.Equal(a.Step(i * 1000L, 1), b.Step(i * 1000L, 1));
    }

    [Fact]
    public void Step_AnyTick_TruePositionOnASegment()
    {
        var graph = TwoSegments();
        var device = new SimulatedDevice("d1", graph, 3);

        for (var i = 0; i < 200; i++)
        {
            device.Step(i * 1000L, 1);
            var best = double.MaxValue;
            for (var n = 0; n < graph.Nodes.Count; n++)
                foreach (var m in graph.Neighbours(n))
                    if (n < m)
                        best = Math.Min(best, DistanceToSegmentM(graph, device.TruePosition, graph.Nodes[n], graph.Nodes[m]));
            Assert.True(best < 0.001, $"tick {i}: {best} m from the nearest edge");
        }
    }

    [Fact]
    public void Step_OneTick_MovesSpeedTimesInterval()
    {
        var graph = Straight();
        var device = new SimulatedDevice("d1", graph, 5);
        var previous = device.TruePosition;

        for (var i = 0; i < 10; i++)
        {
            device.Step(i * 1000L, 1);
            var moved = graph.DistanceM(previous, device.TruePosition);
            Assert.True(Math.Abs(moved - device.SpeedMps * 1.0) < 1e-3, $"tick {i}: moved {moved} m");
            previous = device.TruePosition;
        }
    }

    [Fact]
    public void Step_DeadEnd_TurnsBack()
    {
        // About 10 m line: the device must bounce between its two ends.
        var graph = PathGraph.Load(Map(PathFeature("[[15.070,37.520],[15.070113,37.520]]")));
        var device = new SimulatedDevice("d1", graph, 11);
        var ends = new[] { graph.Nodes[0], graph.Nodes[1] };
        var reachedEnd = new[] { false, false };

        for (var i = 0; i < 200; i++)
        {
            device.Step(i * 1000L, 1);
            var p = device.TruePosition;
            Assert.True(p.Lon >= ends[0].Lon - 1e-12 && p.Lon <= ends[1].Lon + 1e-12, $"tick {i}: left the segment");
            Assert.True(Math.Abs(p.Lat - ends[0].Lat) < 1e-12, $"tick {i}: left the segment");
            for (var e = 0; e < 2; e++)
                if (graph.DistanceM(p, ends[e]) < 2.0) reachedEnd[e] = true;
        }

        Assert.True(reachedEnd[0] && reachedEnd[1]);
    }

    [Fact]
    public void Step_RealMap_AllTruePointsInsidePerimeter()
    {
        var graph = PathGraph.Load(File.ReadAllText(Path.Combine(RepoRoot(), "data", "map.geojson")));
        var devices = Enumerable.Range(0, 10).Select(i => new SimulatedDevice($"d{i}", graph, 42 + i)).ToArray();

        for (var tick = 0; tick < 600; tick++)
            foreach (var device in devices)
            {
                device.Step(tick * 1000L, 1);
                Assert.True(PathGraph.Inside(graph.Perimeter, device.TruePosition),
                    $"{device.DeviceId} tick {tick}: {device.TruePosition} outside perimeter");
            }
    }
}
