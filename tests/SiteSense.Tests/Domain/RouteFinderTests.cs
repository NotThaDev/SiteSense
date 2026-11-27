using SiteSense.Domain.Navigation;

namespace SiteSense.Tests.Domain;

public sealed class RouteFinderTests
{
    private static readonly GraphSettings S = new(1, 0.8, 0.5, 0.5);

    private static readonly IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> NoBlocked = [];

    private static GraphPath P(string id, bool steps, params (double X, double Y)[] pts) => new(id, false, steps, pts);

    private static RouteGraph Build(params GraphPath[] paths) => RouteGraphBuilder.Build(paths, NoBlocked, S);

    private static RouteGraph Line() => Build(P("p", false, (0, 0), (10, 0)));

    [Fact]
    public void Find_DetoursAroundBlockedEdge()
    {
        GraphNode[] nodes = [new(0, 0, 0), new(1, 10, 0), new(2, 5, 5)];
        var len = Math.Sqrt(50);
        GraphEdge[] edges = [new(0, 1, 10, 10, false, "a"), new(0, 2, len, len, true, "b"), new(2, 1, len, len, true, "c")];
        var g = new RouteGraph(nodes, edges, [[0, 1], [0, 2], [1, 2]]);

        var r = RouteFinder.Find(g, (0, 0), (10, 0), 5, 1);

        Assert.Equal(RouteStatus.Ok, r.Status);
        Assert.Equal([(0.0, 0.0), (5.0, 5.0), (10.0, 0.0)], r.Points);
        Assert.Equal(2 * len, r.DistanceM, 6);
        Assert.Equal(2 * len, r.DurationS, 6);
    }

    [Fact]
    public void Find_PrefersFasterOverShorter()
    {
        var g = Build(P("steps", true, (0, 0), (10, 0)), P("paved", false, (0, 0), (5, 3), (10, 0)));

        var r = RouteFinder.Find(g, (0, 0), (10, 0), 5, 1);

        Assert.Equal([(0.0, 0.0), (5.0, 3.0), (10.0, 0.0)], r.Points);
        Assert.Equal(2 * Math.Sqrt(34), r.DurationS, 6);
    }

    [Fact]
    public void Find_StartTooFar()
    {
        var r = RouteFinder.Find(Line(), (5, 52), (5, 0), 30, 1);

        Assert.Equal(RouteStatus.StartTooFar, r.Status);
        Assert.Empty(r.Points);
        Assert.Equal("The start is 52.0 m from the nearest walkable path (max 30 m).", r.Detail);
    }

    [Fact]
    public void Find_DestinationTooFar_IsUnreachable()
    {
        var r = RouteFinder.Find(Line(), (5, 0), (5, 40), 30, 1);

        Assert.Equal(RouteStatus.Unreachable, r.Status);
        Assert.Equal("The destination is 40.0 m from the nearest walkable path (max 30 m).", r.Detail);
    }

    [Fact]
    public void Find_Disconnected_IsUnreachable()
    {
        var g = Build(P("a", false, (0, 0), (10, 0)), P("b", false, (100, 0), (110, 0)));

        var r = RouteFinder.Find(g, (5, 0), (105, 0), 30, 1);

        Assert.Equal(RouteStatus.Unreachable, r.Status);
        Assert.Equal("No walkable route between the start and the destination.", r.Detail);
    }

    [Fact]
    public void Find_SameEdge_BothDirections()
    {
        var forward = RouteFinder.Find(Line(), (2, 0), (8, 0), 5, 1);
        var backward = RouteFinder.Find(Line(), (8, 0), (2, 0), 5, 1);

        Assert.Equal([(2.0, 0.0), (8.0, 0.0)], forward.Points);
        Assert.Equal([(8.0, 0.0), (2.0, 0.0)], backward.Points);
        Assert.Equal(6, forward.DistanceM, 6);
        Assert.Equal(6, backward.DurationS, 6);
    }

    [Fact]
    public void Find_SamePoint_OnlyLegs()
    {
        var r = RouteFinder.Find(Line(), (5, 1), (5, -1), 5, 1);

        Assert.Equal([(5.0, 1.0), (5.0, 0.0), (5.0, -1.0)], r.Points);
        Assert.Equal(2, r.DistanceM, 6);
        Assert.Equal(2, r.DurationS, 6);
    }

    [Fact]
    public void Find_SnapAtEdgeEnd()
    {
        var r = RouteFinder.Find(Line(), (-3, 0), (5, 0), 5, 1);

        Assert.Equal([(-3.0, 0.0), (0.0, 0.0), (5.0, 0.0)], r.Points);
        Assert.Equal(8, r.DistanceM, 6);
    }

    [Fact]
    public void Find_IncludesOffPathLegs()
    {
        var g = Build(P("p", false, (0, 0), (10, 0), (10, 10)));

        var r = RouteFinder.Find(g, (0, -2), (12, 10), 5, 2);

        Assert.Equal([(0.0, -2.0), (0.0, 0.0), (10.0, 0.0), (10.0, 10.0), (12.0, 10.0)], r.Points);
        Assert.Equal(24, r.DistanceM, 6);
        Assert.Equal(20 + 4 / 2.0, r.DurationS, 6); // on-path time uses the graph speed (1 m/s), legs use 2 m/s
    }
}
