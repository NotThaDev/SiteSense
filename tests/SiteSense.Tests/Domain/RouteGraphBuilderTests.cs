using SiteSense.Domain.Navigation;

namespace SiteSense.Tests.Domain;

public sealed class RouteGraphBuilderTests
{
    private static readonly GraphSettings S = new(1.3, 0.8, 0.5, 0.5);

    private static readonly IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> NoBlocked = [];

    private static readonly IReadOnlyList<(double X, double Y)> SquareOuter =
        [(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)];

    private static GraphPath P(string id, params (double X, double Y)[] pts) => new(id, false, false, pts);

    [Fact]
    public void SharedVertex_JoinsPaths()
    {
        var g = RouteGraphBuilder.Build(
            [P("p1", (0, 0), (5, 0), (10, 0)), P("p2", (5, -5), (5, 0), (5, 5))], NoBlocked, S);

        var shared = g.Nodes.Single(n => n.X == 5 && n.Y == 0).Id;
        Assert.Equal(4, g.EdgesOf[shared].Count);
        Assert.Equal(5, g.Nodes.Count);
        Assert.DoesNotContain(g.Edges, e => e.PathId is null);
    }

    [Fact]
    public void EndpointsCloserThanTolerance_GetConnector()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (10, 0)), P("p2", (10.3, 0), (20, 0))], NoBlocked, S);

        var connector = g.Edges.Single(e => e.PathId is null);
        Assert.Equal(0.3, connector.LengthM, 6);
        Assert.True(connector.Traversable);
        Assert.Equal(3, g.Edges.Count);
    }

    [Fact]
    public void EndpointsFartherThanTolerance_StayApart()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (10, 0)), P("p2", (10.7, 0), (20, 0))], NoBlocked, S);

        Assert.DoesNotContain(g.Edges, e => e.PathId is null);
    }

    [Fact]
    public void GapEqualToTolerance_StaysApart()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (10, 0)), P("p2", (10.5, 0), (20, 0))], NoBlocked, S);

        Assert.DoesNotContain(g.Edges, e => e.PathId is null);
    }

    [Fact]
    public void SharedEndpoint_NoConnector()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (10, 0)), P("p2", (10, 0), (20, 0))], NoBlocked, S);

        Assert.Equal(2, g.Edges.Count);
        Assert.DoesNotContain(g.Edges, e => e.PathId is null);
    }

    [Fact]
    public void DuplicateVertex_NoSelfLoop()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (0, 0), (10, 0))], NoBlocked, S);

        Assert.Single(g.Edges);
        Assert.All(g.Edges, e => Assert.NotEqual(e.From, e.To));
    }

    [Fact]
    public void ClosedPath_Builds()
    {
        var g = RouteGraphBuilder.Build([P("p1", (0, 0), (10, 0), (10, 10), (0, 0))], NoBlocked, S);

        Assert.Equal(3, g.Nodes.Count);
        Assert.Equal(3, g.Edges.Count);
    }

    [Fact]
    public void EdgeCrossingHazard_NotTraversable()
    {
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> blocked = [[SquareOuter]];

        var g = RouteGraphBuilder.Build([P("p1", (-10, 5), (-5, 5), (15, 5), (20, 5))], blocked, S);

        var hazard = g.Edges.Single(e => e.LengthM == 20);
        Assert.False(hazard.Traversable);
        Assert.All(g.Edges.Where(e => e != hazard), e => Assert.True(e.Traversable));
    }

    [Fact]
    public void EdgeTouchingRestrictedBoundary_Traversable()
    {
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> blocked = [[SquareOuter]];

        var g = RouteGraphBuilder.Build([P("p1", (-5, 0), (15, 0))], blocked, S);

        Assert.All(g.Edges, e => Assert.True(e.Traversable));
    }

    [Fact]
    public void ConnectorCrossingRestricted_NotTraversable()
    {
        IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> blocked =
            [[[(0.1, -1), (0.2, -1), (0.2, 1), (0.1, 1), (0.1, -1)]]];

        var g = RouteGraphBuilder.Build(
            [P("p1", (-10, 0), (0, 0)), P("p2", (0.3, 0), (10, 0))], blocked, S);

        var connector = g.Edges.Single(e => e.PathId is null);
        Assert.False(connector.Traversable);
        Assert.All(g.Edges.Where(e => e.PathId is not null), e => Assert.True(e.Traversable));
    }

    [Fact]
    public void TimeS_UsesSurfaceAndStepsFactors()
    {
        var stepsUnpaved = new GraphPath("p", true, true, [(0.0, 0.0), (10.0, 0.0)]);
        var paved = new GraphPath("q", false, false, [(0.0, 0.0), (10.0, 0.0)]);

        var g1 = RouteGraphBuilder.Build([stepsUnpaved], NoBlocked, S);
        var g2 = RouteGraphBuilder.Build([paved], NoBlocked, S);

        Assert.Equal(10 / (1.3 * 0.8 * 0.5), g1.Edges.Single().TimeS, 6);
        Assert.Equal(10 / 1.3, g2.Edges.Single().TimeS, 6);
    }
}
