using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SiteSense.Api.Auth;
using SiteSense.Api.Helpers;
using SiteSense.Api.Realtime;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Realtime;

// Uses the static SiteSenseContext.Log, so it shares the "Api" collection and never runs in parallel with API tests.
[Collection("Api")]
public sealed class EventPublisherTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly string[] Aggregates = [SiteSensePolicies.CanSeeAggregates];
    private static readonly string[] Both = [SiteSensePolicies.CanSeeIndividuals, SiteSensePolicies.CanSeeAggregates];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IAppendLog _previousLog;

    public EventPublisherTests()
    {
        _previousLog = SiteSenseContext.Log;
        SiteSenseContext.Log = new FileAppendLog(_root);
    }

    public void Dispose()
    {
        SiteSenseContext.Log = _previousLog;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static EventPublisher Publisher(IEventSender? sender = null) =>
        new(sender ?? new NullEventSender(), NullLogger<EventPublisher>.Instance);

    private static Task<TenantEvent> Occupancy(EventPublisher publisher, string tenantId = "tenant-a") =>
        publisher.PublishAsync(tenantId, "site-1", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

    private static async Task<List<long>> SeqsAsync(string tenantId)
    {
        var records = await SiteSenseContext.Log.ReadRangeAsync<TenantEvent>(tenantId, EventPublisher.LogKey, 0, long.MaxValue, CancellationToken.None);
        return records.Select(e => e.Seq).ToList();
    }

    private static IEnumerable<long> Range(int count) => Enumerable.Range(1, count).Select(i => (long)i);

    [Fact]
    public async Task Publish_PerTenant_SeqsStartAtOneAndAreIndependent()
    {
        var publisher = Publisher();

        var a = new List<long>();
        for (var i = 0; i < 3; i++) a.Add((await Occupancy(publisher, "tenant-a")).Seq);
        var b = await Occupancy(publisher, "tenant-b");

        Assert.Equal([1L, 2L, 3L], a);
        Assert.Equal(1, b.Seq);
    }

    [Fact]
    public async Task Publish_FiftyInParallel_SeqsAreOneToFiftyWithNoGapOrRepeat()
    {
        var publisher = Publisher();

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Occupancy(publisher)));

        Assert.Equal(Range(50), await SeqsAsync("tenant-a"));
    }

    [Fact]
    public async Task Publish_NewPublisherOnSameRoot_ContinuesAfterLastSeq()
    {
        await Occupancy(Publisher());
        await Occupancy(Publisher());

        var next = await Occupancy(Publisher());

        Assert.Equal(3, next.Seq);
    }

    [Fact]
    public async Task Publish_WholeRecordWithoutNewline_NextSeqFollowsAndEachSeqOnce()
    {
        var first = Publisher();
        for (var i = 0; i < 4; i++) await Occupancy(first);
        var file = Directory.GetFiles(_root, "*.jsonl", SearchOption.AllDirectories).Single();
        var torn = JsonSerializer.Serialize(new TenantEvent(5, EventTypes.OccupancyChanged, "site-1", 500,
            JsonSerializer.SerializeToElement(new { areas = Array.Empty<int>() }, Web)), Web);
        File.AppendAllText(file, torn); // whole JSON line, no trailing newline

        var next = await Occupancy(Publisher());

        Assert.Equal(6, next.Seq);
        Assert.Equal(Range(6), await SeqsAsync("tenant-a"));
    }

    [Fact]
    public async Task Publish_EventIsReadableInsideSendAsync()
    {
        var visible = new List<bool>();
        var sender = new DelegateSender(async (tenantId, evt, ct) =>
        {
            var logged = await SiteSenseContext.Log.ReadRangeAsync<TenantEvent>(tenantId, EventPublisher.LogKey,
                evt.Seq * TenantEvent.SeqScale, (evt.Seq + 1) * TenantEvent.SeqScale, ct);
            visible.Add(logged.Any(e => e.Seq == evt.Seq));
        });

        await Occupancy(Publisher(sender));

        Assert.Equal(new[] { true }, visible);
    }

    [Fact]
    public async Task Publish_SenderThrows_ReturnsEventIsSavedAndNextSeqFollows()
    {
        var publisher = Publisher(new DelegateSender((_, _, _) => throw new InvalidOperationException("boom")));

        var first = await Occupancy(publisher);
        var second = await Occupancy(publisher);

        Assert.Equal(1, first.Seq);
        Assert.Equal(2, second.Seq);
        Assert.Equal(Range(2), await SeqsAsync("tenant-a"));
    }

    [Fact]
    public async Task Publish_SenderNeverCompletes_TimesOutAndNextSeqFollows()
    {
        var publisher = new EventPublisher(
            new DelegateSender(async (_, _, ct) => await Task.Delay(Timeout.Infinite, ct)),
            NullLogger<EventPublisher>.Instance)
        { SendTimeout = TimeSpan.FromMilliseconds(100) };

        var first = await Occupancy(publisher).WaitAsync(TimeSpan.FromSeconds(5));
        var second = await Occupancy(publisher).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, first.Seq);
        Assert.Equal(2, second.Seq);
        Assert.Equal(Range(2), await SeqsAsync("tenant-a"));
    }

    [Fact]
    public async Task ReadAfter_AggregatesOnly_ReturnsOccupancyOnlyWithLastSeq()
    {
        var publisher = Publisher();
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await Occupancy(publisher);
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);

        var page = await publisher.ReadAfterAsync("tenant-a", 0, Aggregates, CancellationToken.None);

        Assert.Equal(EventTypes.OccupancyChanged, Assert.Single(page.Items).Type);
        Assert.Equal(3, page.LastSeq);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ReadAfter_BothPolicies_ReturnsAllThree()
    {
        var publisher = Publisher();
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await Occupancy(publisher);
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);

        var page = await publisher.ReadAfterAsync("tenant-a", 0, Both, CancellationToken.None);

        Assert.Equal(3, page.Items.Count);
        Assert.Equal(3, page.LastSeq);
    }

    [Fact]
    public async Task ReadAfter_WindowOfOnlySafetyEvents_EmptyItemsButLastSeqAdvances()
    {
        var publisher = Publisher();
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", "site-1", EventTypes.PositionUpdated, new { deviceId = "d2" }, CancellationToken.None);

        var page = await publisher.ReadAfterAsync("tenant-a", 0, Aggregates, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(2, page.LastSeq);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ReadAfter_MoreThanOnePage_WindowsOf1000()
    {
        var publisher = Publisher();
        for (var i = 0; i < 1005; i++) await Occupancy(publisher);

        var first = await publisher.ReadAfterAsync("tenant-a", 0, Aggregates, CancellationToken.None);
        var second = await publisher.ReadAfterAsync("tenant-a", 1000, Aggregates, CancellationToken.None);

        Assert.Equal(1000, first.Items.Count);
        Assert.Equal(1000, first.LastSeq);
        Assert.True(first.HasMore);
        Assert.Equal(5, second.Items.Count);
        Assert.Equal(1005, second.LastSeq);
        Assert.False(second.HasMore);
    }

    private sealed class DelegateSender(Func<string, TenantEvent, CancellationToken, Task> send) : IEventSender
    {
        public Task SendAsync(string tenantId, TenantEvent evt, CancellationToken ct) => send(tenantId, evt, ct);
    }
}
