namespace SiteSense.Domain.Navigation;

public sealed record GraphNode(int Id, double X, double Y);

// PathId is null for a tolerance connector.
public sealed record GraphEdge(int From, int To, double LengthM, double TimeS, bool Traversable, string? PathId);

public sealed record GraphPath(string Id, bool Unpaved, bool Steps, IReadOnlyList<(double X, double Y)> Points);

public sealed record GraphSettings(double WalkingSpeedMps, double UnpavedSpeedFactor, double StepsSpeedFactor, double ConnectToleranceM);

/// <summary>Undirected pedestrian graph in local metres (MAP-03). Built once per map version; read-only.</summary>
public sealed class RouteGraph(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges, IReadOnlyList<IReadOnlyList<int>> edgesOf)
{
    public IReadOnlyList<GraphNode> Nodes { get; } = nodes;
    public IReadOnlyList<GraphEdge> Edges { get; } = edges;
    /// <summary>Indices into Edges of the edges touching each node, for both directions.</summary>
    public IReadOnlyList<IReadOnlyList<int>> EdgesOf { get; } = edgesOf;
}
