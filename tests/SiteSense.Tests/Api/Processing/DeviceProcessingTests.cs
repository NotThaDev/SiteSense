using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Processing;

[Collection("Api")]
public sealed class DeviceProcessingTests : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string Device = "dev-1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _previous;

    public DeviceProcessingTests()
    {
        _previous = SiteSenseContext.Store;
        SiteSenseContext.Store = new FileDocumentStore(_root);
    }

    public void Dispose()
    {
        SiteSenseContext.Store = _previous;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task MarkDirty_NoDoc_CreatesWithFromMs()
    {
        await DeviceProcessing.MarkDirtyAsync(Tenant, Device, 5000, CancellationToken.None);

        var doc = await DeviceProcessing.GetAsync(Tenant, Device, CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(5000L, doc.DirtyFromMs);
        Assert.Equal(1L, doc.Version);
        Assert.Null(doc.CursorMs);
        Assert.Equal(0L, doc.Revision);
    }

    [Fact]
    public async Task MarkDirty_OlderTimestamp_LowersDirtyFrom()
    {
        await DeviceProcessing.MarkDirtyAsync(Tenant, Device, 5000, CancellationToken.None);

        await DeviceProcessing.MarkDirtyAsync(Tenant, Device, 3000, CancellationToken.None);

        var doc = await DeviceProcessing.GetAsync(Tenant, Device, CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(3000L, doc.DirtyFromMs);
    }

    [Fact]
    public async Task MarkDirty_NewerTimestamp_KeepsMinimumButBumpsVersion()
    {
        await DeviceProcessing.MarkDirtyAsync(Tenant, Device, 5000, CancellationToken.None);

        await DeviceProcessing.MarkDirtyAsync(Tenant, Device, 9000, CancellationToken.None);

        var doc = await DeviceProcessing.GetAsync(Tenant, Device, CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(5000L, doc.DirtyFromMs);
        Assert.Equal(2L, doc.Version);
    }

    [Fact]
    public async Task MarkDirty_Concurrent_KeepsMinimum()
    {
        var marks = Enumerable.Range(0, 20)
            .Select(i => DeviceProcessing.MarkDirtyAsync(Tenant, Device, 1000 + i, CancellationToken.None));

        await Task.WhenAll(marks);

        var doc = await DeviceProcessing.GetAsync(Tenant, Device, CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(1000L, doc.DirtyFromMs);
        Assert.Equal(20L, doc.Version);
    }
}
