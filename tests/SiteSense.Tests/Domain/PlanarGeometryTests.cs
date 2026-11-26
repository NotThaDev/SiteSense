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

    private const double Tolerance = 1e-6;

    private static readonly IReadOnlyList<(double X, double Y)> Square = [(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)];

    private static readonly IReadOnlyList<(double X, double Y)> Hole = [(4, 4), (6, 4), (6, 6), (4, 6), (4, 4)];

    private static readonly IReadOnlyList<(double X, double Y)> UShape =
        [(0, 0), (10, 0), (10, 10), (7, 10), (7, 3), (3, 3), (3, 10), (0, 10), (0, 0)];

    [Fact]
    public void Locate_PointInsideSquare_ReturnsInside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square];

        var result = PlanarGeometry.Locate((2, 2), rings, Tolerance);

        Assert.Equal(PointLocation.Inside, result);
    }

    [Fact]
    public void Locate_PointOutsideSquare_ReturnsOutside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square];

        var result = PlanarGeometry.Locate((12, 5), rings, Tolerance);

        Assert.Equal(PointLocation.Outside, result);
    }

    [Fact]
    public void Locate_PointOnEdge_ReturnsOnBoundary()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square];

        var result = PlanarGeometry.Locate((10, 5), rings, Tolerance);

        Assert.Equal(PointLocation.OnBoundary, result);
    }

    [Fact]
    public void Locate_PointOnVertex_ReturnsOnBoundary()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square];

        var result = PlanarGeometry.Locate((0, 0), rings, Tolerance);

        Assert.Equal(PointLocation.OnBoundary, result);
    }

    [Fact]
    public void Locate_PointInsideHole_ReturnsOutside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square, Hole];

        var result = PlanarGeometry.Locate((5, 5), rings, Tolerance);

        Assert.Equal(PointLocation.Outside, result);
    }

    [Fact]
    public void Locate_PointOnHoleEdge_ReturnsOnBoundary()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [Square, Hole];

        var result = PlanarGeometry.Locate((4, 5), rings, Tolerance);

        Assert.Equal(PointLocation.OnBoundary, result);
    }

    [Fact]
    public void Locate_PointInConcaveNotch_ReturnsOutside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [UShape];

        var result = PlanarGeometry.Locate((5, 8), rings, Tolerance);

        Assert.Equal(PointLocation.Outside, result);
    }

    [Fact]
    public void Locate_PointInConcaveBase_ReturnsInside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [UShape];

        var result = PlanarGeometry.Locate((5, 1), rings, Tolerance);

        Assert.Equal(PointLocation.Inside, result);
    }

    [Fact]
    public void Locate_OpenRing_TreatsAsClosed()
    {
        IReadOnlyList<(double X, double Y)> open = [(0, 0), (10, 0), (10, 10), (0, 10)];
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [open];

        var inside = PlanarGeometry.Locate((2, 2), rings, Tolerance);
        var onClosingEdge = PlanarGeometry.Locate((0, 5), rings, Tolerance);

        Assert.Equal(PointLocation.Inside, inside);
        Assert.Equal(PointLocation.OnBoundary, onClosingEdge);
    }

    [Fact]
    public void Locate_NoRings_ReturnsOutside()
    {
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings = [];

        var result = PlanarGeometry.Locate((2, 2), rings, Tolerance);

        Assert.Equal(PointLocation.Outside, result);
    }
}
