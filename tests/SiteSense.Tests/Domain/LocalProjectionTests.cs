using SiteSense.Domain.Geometry;
using Xunit;

namespace SiteSense.Tests.Domain;

public class LocalProjectionTests
{
    [Theory]
    [InlineData(15.0705, 37.5250, 15.0720, 37.5262)]
    [InlineData(9.19, 45.46, 9.20, 45.47)]
    public void RoundTrip_ErrorBelowOneCentimeter(double lon0, double lat0, double lon, double lat)
    {
        var p = new LocalProjection(lon0, lat0);
        var (x, y) = p.ToLocal(lon, lat);
        var (lon2, lat2) = p.ToWgs84(x, y);
        var (x2, y2) = p.ToLocal(lon2, lat2);
        Assert.True(Math.Abs(x - x2) < 0.01 && Math.Abs(y - y2) < 0.01);
    }

    [Fact]
    public void KnownDistance_AlongEquator_MatchesEllipsoidArc()
    {
        var p = new LocalProjection(0, 0);
        var (x, y) = p.ToLocal(0.001, 0);
        Assert.InRange(x, 111.3195 - 0.01, 111.3195 + 0.01); // a * pi/180 * 0.001
        Assert.Equal(0, y, 9);
    }

    [Fact]
    public void KnownDistance_OneArcMinuteLatitudeAt45_IsAboutOneNauticalMile()
    {
        var p = new LocalProjection(0, 45);
        var (_, y) = p.ToLocal(0, 45 + 1.0 / 60);
        Assert.InRange(y, 1852.2 - 0.5, 1852.2 + 0.5); // meridian arc per minute at 45°
    }

    [Fact]
    public void FromCentroid_OriginMapsToZero()
    {
        var p = LocalProjection.FromCentroid([(10, 40), (10.002, 40), (10.002, 40.002), (10, 40.002)]);
        var (x, y) = p.ToLocal(10.001, 40.001);
        Assert.True(Math.Abs(x) < 1e-6 && Math.Abs(y) < 1e-6);
    }
}
