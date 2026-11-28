using SiteSense.Api.Models;
using SiteSense.Api.Services;
using SiteSense.Domain.Geometry;

namespace SiteSense.Tests.Api.Processing;

// SS-24, Q26: the plausibility and noise filter, pure, no store.
public sealed class TrackFilterTests
{
    private const long T0 = 1793268000000;
    private const string Dev = "dev-1";
    private static readonly LocalProjection Frame = new(9.2, 45.5);
    private static readonly TrackingOptions Options = new();

    // A position eastM and northM metres from the frame origin, sec seconds after T0.
    private static Position At(double sec, double eastM, double northM, double accuracyM = 5)
    {
        var (lon, lat) = Frame.ToWgs84(eastM, northM);
        return new Position(Dev, T0 + (long)(sec * 1000), lat, lon, accuracyM);
    }

    [Fact]
    public void Jump_IsDropped()
    {
        var track = TrackFilter.Apply(null, [At(0, 0, 0), At(1, 2000, 0)], 0, Options);

        Assert.Equal([false, true], track.Select(t => t.Dropped));
    }

    [Fact]
    public void Noise_IsSmoothed_WeightedByAccuracy()
    {
        // Weights 1/1 and 1/16: the new point moves the track 1/17 of the way (1.7 m / 17 = 0.1 m).
        var track = TrackFilter.Apply(null, [At(0, 0, 0, 1), At(1, 1.7, 0, 4)], 0, Options);

        Assert.False(track[1].Dropped);
        var (x, y) = Frame.ToLocal(track[1].Lon, track[1].Lat);
        Assert.Equal(0.1, x, 6);
        Assert.Equal(0, y, 6);
        Assert.Equal(4, track[1].AccuracyM);
    }

    [Fact]
    public void ThreeConsistentDrops_RestartFromTheLastOne()
    {
        var raw = new[] { At(0, 0, 0), At(1, 1000, 0), At(2, 1000, 0), At(3, 1000, 0), At(4, 1000, 0) };

        var track = TrackFilter.Apply(null, raw, 0, Options);

        Assert.Equal([false, true, true, false, false], track.Select(t => t.Dropped));
        // The restart point is kept as is, not smoothed.
        Assert.Equal(raw[3].Lon, track[3].Lon);
        Assert.Equal(raw[3].Lat, track[3].Lat);
    }

    [Fact]
    public void SameTimestamp_IsSkipped()
    {
        var track = TrackFilter.Apply(null, [At(0, 0, 0), At(0, 2000, 0)], 0, Options);

        Assert.Single(track);
        Assert.False(track[0].Dropped);
    }

    [Fact]
    public void FirstPoint_IsKeptAsIs()
    {
        var raw = At(0, 3, 4, 6);

        var track = TrackFilter.Apply(null, [raw], 0, Options);

        Assert.Equal(new TrackPoint(Dev, raw.TimestampMs, raw.Lat, raw.Lon, 6, 0), Assert.Single(track));
    }

    [Fact]
    public void AccuracyZero_GivesNoNaN()
    {
        var track = TrackFilter.Apply(null, [At(0, 0, 0, 0), At(1, 0.5, 0, 0)], 0, Options);

        Assert.All(track, t =>
        {
            Assert.True(double.IsFinite(t.Lat));
            Assert.True(double.IsFinite(t.Lon));
        });
        Assert.Equal(1, track[1].AccuracyM);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void ReplayFromMiddle_EqualsInOrder(int split)
    {
        var raw = new[] { At(0, 0, 0), At(1, 1, 1), At(2, 2000, 0), At(3, 3, 3), At(4, 4, 4), At(5, 5000, 0), At(6, 6, 6) };
        var inOrder = TrackFilter.Apply(null, raw, 1, Options);
        var seed = inOrder.Take(split).Last(t => !t.Dropped);

        var replay = TrackFilter.Apply(seed, raw.Skip(split).ToList(), 1, Options);

        Assert.Equal(inOrder.Skip(split), replay);
    }
}
