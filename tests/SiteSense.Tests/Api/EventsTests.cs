using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SiteSense.Api.Realtime;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class EventsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public EventsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Viewer_SeesOnlyOccupancy()
    {
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Viewer"]));
        var after = await CurrentSeqAsync(client);
        await publisher.PublishAsync("tenant-a", "events-viewer", EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", "events-viewer", EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", "events-viewer", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

        var body = await GetEventsAsync(client, after);

        var types = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { EventTypes.OccupancyChanged }, types);
    }

    [Fact]
    public async Task Get_Safety_SeesAllThreeTypes()
    {
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Safety"]));
        var after = await CurrentSeqAsync(client);
        await publisher.PublishAsync("tenant-a", "events-safety", EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", "events-safety", EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", "events-safety", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

        var body = await GetEventsAsync(client, after);

        var types = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("type").GetString()).ToList();
        Assert.Equal(new[] { EventTypes.PositionUpdated, EventTypes.AlertRaised, EventTypes.OccupancyChanged }, types);
    }

    [Fact]
    public async Task Get_OtherTenant_NeverSeesTenantAEvents()
    {
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var clientB = _factory.CreateClient().WithToken(TestTokens.Create("tenant-b", ["Safety"]));
        var after = await CurrentSeqAsync(clientB);
        await publisher.PublishAsync("tenant-a", "events-tenant", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

        var body = await GetEventsAsync(clientB, after);

        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Get_AfterParameter_ReturnsOnlyLaterEvents()
    {
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Viewer"]));
        var first = await publisher.PublishAsync("tenant-a", "events-after", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);
        var second = await publisher.PublishAsync("tenant-a", "events-after", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

        var body = await GetEventsAsync(client, first.Seq);

        var seqs = body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("seq").GetInt64()).ToList();
        Assert.DoesNotContain(first.Seq, seqs);
        Assert.Contains(second.Seq, seqs);
    }

    [Fact]
    public async Task Get_NegativeAfter_Returns400()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Viewer"]));

        var response = await client.GetAsync("/api/v1/events?after=-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Get_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_GatewayToken_Returns403()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", scope: "positions:write"));

        var response = await client.GetAsync("/api/v1/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ResponseShape_UsesExactPropertyNames()
    {
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Safety"]));
        var after = await CurrentSeqAsync(client);
        await publisher.PublishAsync("tenant-a", "events-shape", EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);

        var body = await GetEventsAsync(client, after);

        Assert.Equal(new[] { "items", "lastSeq", "hasMore" }, body.EnumerateObject().Select(p => p.Name));
        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(new[] { "seq", "type", "siteId", "atMs", "data" }, item.EnumerateObject().Select(p => p.Name));
    }

    // Walks the catch-up pages to the current last seq of the tenant (the host is shared by the tests).
    private static async Task<long> CurrentSeqAsync(HttpClient client)
    {
        long after = 0;
        while (true)
        {
            var body = await GetEventsAsync(client, after);
            after = body.GetProperty("lastSeq").GetInt64();
            if (!body.GetProperty("hasMore").GetBoolean()) return after;
        }
    }

    private static async Task<JsonElement> GetEventsAsync(HttpClient client, long after)
    {
        var response = await client.GetAsync($"/api/v1/events?after={after}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }
}
