using SiteSense.Domain.Geometry;

namespace SiteSense.Tests.Domain;

public sealed class PlanarGeometryTests
{
    private static readonly IReadOnlyList<(double X, double Y)> Line = [(0, 0), (10, 0)];

    [Fact]
    public void PointOnSegmentMiddle_IsZero()
    {
        Assert.Equal(0, PlanarGeometry.DistanceToPolyline((5, 0), Line), 9);
    }

    [Fact]
    public void PointBeyondEnd_IsDistanceToEnd()
    {
        Assert.Equal(5, PlanarGeometry.DistanceToPolyline((13, 4), Line), 9);
    }

    [Fact]
    public void PointOnVertex_IsZero()
    {
        Assert.Equal(0, PlanarGeometry.DistanceToPolyline((10, 0), Line), 9);
    }

    [Fact]
    public void ZeroLengthSegment_IsDistanceToPoint()
    {
        IReadOnlyList<(double X, double Y)> degenerate = [(1, 1), (1, 1)];

        Assert.Equal(5, PlanarGeometry.DistanceToPolyline((4, 5), degenerate), 9);
    }

    [Fact]
    public void PerpendicularPoint_IsPerpendicularDistance()
    {
        Assert.Equal(3, PlanarGeometry.DistanceToPolyline((5, 3), Line), 9);
    }

    [Fact]
    public void EmptyLine_IsPositiveInfinity()
    {
        Assert.Equal(double.PositiveInfinity, PlanarGeometry.DistanceToPolyline((0, 0), []));
    }
}
