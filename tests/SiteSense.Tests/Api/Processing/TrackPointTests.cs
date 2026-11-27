using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Processing;

[Collection("Api")]
public sealed class TrackPointTests : IDisposable
{
    private const long T0 = 1793268000000; // aligned to a UTC hour boundary
    private const string Tenant = "tenant-a";
    private const string Device = "dev-1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IAppendLog _previous;

    public TrackPointTests()
    {
        _previous = SiteSenseContext.Log;
        SiteSenseContext.Log = new FileAppendLog(_root);
    }

    public void Dispose()
    {
        SiteSenseContext.Log = _previous;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static TrackPoint TP(long ts, long rev = 1, double lat = 45.5, string device = Device) =>
        new(device, T0 + ts, lat, 9.2, 4.5, rev);

    [Fact]
    public async Task Read_TwoRevisions_HighestWinsPerTimestamp()
    {
        await TrackPoint.AppendAsync(Tenant, Device, [TP(1000), TP(2000), TP(3000)], CancellationToken.None);
        await TrackPoint.AppendAsync(Tenant, Device, [TP(2000, rev: 2, lat: 46.0), TP(3000, rev: 2, lat: 46.0)], CancellationToken.None);

        var read = await TrackPoint.ReadAsync(Tenant, Device, 0, long.MaxValue, CancellationToken.None);

        Assert.Equal(3, read.Count);
        Assert.Equal(T0 + 1000, read[0].TimestampMs);
        Assert.Equal(1L, read[0].Revision);
        Assert.Equal(T0 + 2000, read[1].TimestampMs);
        Assert.Equal(2L, read[1].Revision);
        Assert.Equal(46.0, read[1].Lat);
        Assert.Equal(T0 + 3000, read[2].TimestampMs);
        Assert.Equal(2L, read[2].Revision);
    }

    [Fact]
    public async Task Read_RepeatedRecord_ReadsOnce()
    {
        await TrackPoint.AppendAsync(Tenant, Device, [TP(1000)], CancellationToken.None);
        await TrackPoint.AppendAsync(Tenant, Device, [TP(1000)], CancellationToken.None);

        var read = await TrackPoint.ReadAsync(Tenant, Device, 0, long.MaxValue, CancellationToken.None);

        Assert.Single(read);
    }

    [Fact]
    public async Task Read_OutOfOrderAppend_OrderedByTimestamp()
    {
        await TrackPoint.AppendAsync(Tenant, Device, [TP(3000), TP(1000), TP(2000)], CancellationToken.None);

        var read = await TrackPoint.ReadAsync(Tenant, Device, 0, long.MaxValue, CancellationToken.None);

        Assert.Equal([T0 + 1000, T0 + 2000, T0 + 3000], read.Select(p => p.TimestampMs));
    }

    [Fact]
    public async Task Append_ForeignDevice_Throws()
    {
        var foreign = new[] { TP(1000, device: "dev-2") };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            TrackPoint.AppendAsync(Tenant, Device, foreign, CancellationToken.None));
    }
}
