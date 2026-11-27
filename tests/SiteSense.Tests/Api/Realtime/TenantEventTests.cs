using System.Text.Json;
using SiteSense.Api.Realtime;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Realtime;

public sealed class TenantEventTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    private static TenantEvent Event(long seq) =>
        new(seq, EventTypes.PositionUpdated, "site-1", 1000 + seq, JsonSerializer.SerializeToElement(new { n = seq }, Options));

    [Fact]
    public void RoundTrip_SameValues_NoTimestampField()
    {
        var data = JsonSerializer.SerializeToElement(new { areaId = "a1", count = 3 }, Options);
        var e = new TenantEvent(7, EventTypes.OccupancyChanged, "site-1", 1234, data);

        var json = JsonSerializer.Serialize(e, Options);
        var back = JsonSerializer.Deserialize<TenantEvent>(json, Options)!;

        Assert.Contains("\"seq\"", json);
        Assert.DoesNotContain("timestampMs", json);
        Assert.Equal(e.Seq, back.Seq);
        Assert.Equal(e.Type, back.Type);
        Assert.Equal(e.SiteId, back.SiteId);
        Assert.Equal(e.AtMs, back.AtMs);
        Assert.Equal(e.Data.GetRawText(), back.Data.GetRawText());
    }

    [Fact]
    public async Task Store_TwoEvents_ReadRangeReturnsBothBySeq()
    {
        var log = new FileAppendLog(_root);
        TenantEvent[] events = [Event(1), Event(2)];
        await log.AppendAsync("tenant-a", "events", events, CancellationToken.None);

        var read = await log.ReadRangeAsync<TenantEvent>("tenant-a", "events",
            1 * TenantEvent.SeqScale, 3 * TenantEvent.SeqScale, CancellationToken.None);

        Assert.Equal([1L, 2L], read.Select(e => e.Seq));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
