using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Services;
using SiteSense.Domain.Geometry;

namespace SiteSense.Tests.Api.Processing;

// SS-25, Q27, Q28, Q119: area hysteresis and the candidate area. Pure, no store.
public sealed class AreaAssignerTests
{
    private const long T0 = 1793268000000;
    private const string Dev = "dev-1";
    private static readonly LocalProjection Frame = new(15.07, 37.52);
    private static readonly TrackingOptions Options = new();

    // Metres from Frame. Rings are left open (Edges closes them); the symmetric perimeter then has Frame's origin as centroid.
    private static readonly SiteAreas Main = Areas(
        Area("perimeter", "perimeter", Box(-300, -300, 300, 300)),
        Area("a", "work", Box(0, 0, 100, 100)),
        Area("b", "work", Box(150, 0, 250, 100)),
        Area("c", "work", Box(-250, 0, -150, 100)));

    // "a" and "o" overlap on x 50..100; "a" comes first in map order.
    private static readonly SiteAreas Overlap = Areas(
        Area("perimeter", "perimeter", Box(-300, -300, 300, 300)),
        Area("a", "work", Box(0, 0, 100, 100)),
        Area("o", "work", Box(50, 0, 200, 100)));

    private static (double X, double Y)[] Box(double x0, double y0, double x1, double y1) => [(x0, y0), (x1, y0), (x1, y1), (x0, y1)];

    private static MapArea Area(string id, string type, (double X, double Y)[] ring) =>
        new(id, id, type, null, [ring.Select(p => { var (lon, lat) = Frame.ToWgs84(p.X, p.Y); return new[] { lon, lat }; }).ToArray()]);

    private static SiteAreas Areas(params MapArea[] areas) =>
        SiteAreas.From(new SiteMap { Id = "site-01", TenantId = "tenant-a", Areas = areas.ToList() })!;

    // A point eastM and northM metres from Frame, sec seconds after T0.
    private static TrackPoint TP(double sec, double eastM, double northM, bool dropped = false)
    {
        var (lon, lat) = Frame.ToWgs84(eastM, northM);
        return new TrackPoint(Dev, T0 + (long)(sec * 1000), lat, lon, 5, 0, dropped);
    }

    private static IReadOnlyList<string?> Ids(string? seed, SiteAreas areas, params TrackPoint[] points) =>
        AreaAssigner.Assign(seed, points, areas, Options).Select(p => p.AreaId).ToList();

    [Fact]
    public void FirstPoint_TakesItsCandidate()
    {
        Assert.Equal(new string?[] { "a" }, Ids(null, Main, TP(0, 50, 50)));
    }

    [Fact]
    public void FirstPoint_OutsideSubAreas_TakesPerimeter()
    {
        Assert.Equal(new string?[] { "perimeter" }, Ids(null, Main, TP(0, -100, -200)));
    }

    [Fact]
    public void BorderNoise_KeepsArea()
    {
        // Fixes 3 m either side of c's east edge (x = -150), accuracy 5 m: the margin keeps c.
        var points = Enumerable.Range(0, 6).Select(i => TP(i, i % 2 == 0 ? -147 : -153, 50)).ToArray();

        Assert.Equal(Enumerable.Repeat<string?>("c", 6), Ids("c", Main, points));
    }

    [Fact]
    public void DeepMove_SwitchesAtOnce()
    {
        // 50 m inside b, far outside c: inside the candidate by more than the margin, so it switches at once.
        Assert.Equal(new string?[] { "c", "b" }, Ids("c", Main, TP(0, -200, 50), TP(1, 200, 50)));
    }

    [Fact]
    public void SlowDrift_SwitchesAfterConfirmPoints()
    {
        // 3 m inside a: not inside by the margin, so the third point in a row switches.
        var points = Enumerable.Range(0, 3).Select(i => TP(i, 3, 50)).ToArray();

        Assert.Equal(new string?[] { "perimeter", "perimeter", "a" }, Ids("perimeter", Main, points));
    }

    [Fact]
    public void AToBToC_RestartsTheCount()
    {
        // Each area is named once or twice in a row, so only the final run of three in a switches.
        var points = new[] { TP(0, 3, 50), TP(1, 152, 50), TP(2, -152, 50), TP(3, 3, 50), TP(4, 3, 50), TP(5, 3, 50) };

        Assert.Equal(new string?[] { "perimeter", "perimeter", "perimeter", "perimeter", "perimeter", "a" }, Ids("perimeter", Main, points));
    }

    [Fact]
    public void Tombstone_IsPassedThroughAndNotCounted()
    {
        var points = new[] { TP(0, 3, 50), TP(1, 3, 50, dropped: true), TP(2, 3, 50), TP(3, 3, 50) };

        Assert.Equal(new string?[] { "perimeter", null, "perimeter", "a" }, Ids("perimeter", Main, points));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("gone")]
    public void UnknownOrNullSeed_CountsAsNoArea(string? seed)
    {
        Assert.Equal(new string?[] { "a" }, Ids(seed, Main, TP(0, 50, 50)));
    }

    [Fact]
    public void PointJustOutsidePerimeter_GetsPerimeterId()
    {
        Assert.Equal(new string?[] { "perimeter" }, Ids(null, Main, TP(0, 320, 0)));
    }

    [Fact]
    public void DeepInCurrentButAlsoInEarlierArea_Stays()
    {
        // (75, 50) is in a (earlier) and deep in o (current): o stays.
        var points = new[] { TP(0, 75, 50), TP(1, 75, 50), TP(2, 75, 50) };

        Assert.Equal(Enumerable.Repeat<string?>("o", 3), Ids("o", Overlap, points));
    }

    [Fact]
    public void NoAreas_LeavesAreaIdNull()
    {
        var points = new[] { TP(0, 50, 50) };

        Assert.Equal(new string?[] { null }, AreaAssigner.Assign(null, points, null, Options).Select(p => p.AreaId));
    }
}

public sealed class SiteAreasTests
{
    private static readonly LocalProjection Frame = new(15.07, 37.52);

    private static (double Lon, double Lat) At(double eastM, double northM) => Frame.ToWgs84(eastM, northM);

    [Fact]
    public void Candidate_InsideSubArea_ReturnsSubArea()
    {
        var (lon, lat) = At(50, 50);

        Assert.Equal("a", Overlap().Candidate(lon, lat));
    }

    [Fact]
    public void Candidate_InsidePerimeterOnly_ReturnsPerimeter()
    {
        var (lon, lat) = At(-100, -200);

        Assert.Equal("perimeter", Overlap().Candidate(lon, lat));
    }

    [Fact]
    public void Candidate_Overlap_ReturnsFirstInMapOrder()
    {
        var (lon, lat) = At(75, 50);

        Assert.Equal("a", Overlap().Candidate(lon, lat));
    }

    [Fact]
    public void From_NoPerimeter_ReturnsNull()
    {
        var map = new SiteMap { Areas = [new MapArea("a", "a", "work", null, [[[15.07, 37.52], [15.08, 37.52], [15.08, 37.53]]])] };

        Assert.Null(SiteAreas.From(map));
    }

    private static SiteAreas Overlap()
    {
        var perimeter = new[] { (-300.0, -300.0), (300.0, -300.0), (300.0, 300.0), (-300.0, 300.0) };
        var a = new[] { (0.0, 0.0), (100.0, 0.0), (100.0, 100.0), (0.0, 100.0) };
        var o = new[] { (50.0, 0.0), (200.0, 0.0), (200.0, 100.0), (50.0, 100.0) };
        return SiteAreas.From(new SiteMap { Areas = [Area("perimeter", "perimeter", perimeter), Area("a", "work", a), Area("o", "work", o)] })!;
    }

    private static MapArea Area(string id, string type, (double X, double Y)[] ring) =>
        new(id, id, type, null, [ring.Select(p => { var (lon, lat) = At(p.X, p.Y); return new[] { lon, lat }; }).ToArray()]);
}
