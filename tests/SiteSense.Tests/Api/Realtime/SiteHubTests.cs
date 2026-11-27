using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using SiteSense.Api.Realtime;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Realtime;

[Collection("Api")]
public sealed class SiteHubTests : IClassFixture<ApiFactory>
{
    // The real site from SS-18; each test swaps in its own siteId.
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    private readonly ApiFactory _factory;

    public SiteHubTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Safety_Join_ReceivesEachTypeOnce()
    {
        var siteId = await CreateSiteAsync("tenant-a");
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        await using var connection = HubClient.Create(_factory, TestTokens.Create("tenant-a", ["Safety"]));
        var positions = new Inbox(connection, EventTypes.PositionUpdated);
        var alerts = new Inbox(connection, EventTypes.AlertRaised);
        var occupancy = new Inbox(connection, EventTypes.OccupancyChanged);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSite", siteId);

        await publisher.PublishAsync("tenant-a", siteId, EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", siteId, EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", siteId, EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);
        await positions.NextAsync();
        await alerts.NextAsync();
        await occupancy.NextAsync();
        await MarkerAsync(publisher, "tenant-a", siteId, occupancy);

        Assert.Equal(0, positions.Count);
        Assert.Equal(0, alerts.Count);
        Assert.Equal(0, occupancy.Count);
    }

    [Fact]
    public async Task Viewer_Join_ReceivesOnlyOccupancy()
    {
        var siteId = await CreateSiteAsync("tenant-a");
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        await using var connection = HubClient.Create(_factory, TestTokens.Create("tenant-a", ["Viewer"]));
        var positions = new Inbox(connection, EventTypes.PositionUpdated);
        var alerts = new Inbox(connection, EventTypes.AlertRaised);
        var occupancy = new Inbox(connection, EventTypes.OccupancyChanged);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSite", siteId);

        await publisher.PublishAsync("tenant-a", siteId, EventTypes.PositionUpdated, new { deviceId = "d1" }, CancellationToken.None);
        await publisher.PublishAsync("tenant-a", siteId, EventTypes.AlertRaised, new { alertId = "x" }, CancellationToken.None);
        await MarkerAsync(publisher, "tenant-a", siteId, occupancy);

        Assert.Equal(0, positions.Count);
        Assert.Equal(0, alerts.Count);
    }

    [Fact]
    public async Task Envelope_OnHub_EqualsCatchUpItem()
    {
        var siteId = await CreateSiteAsync("tenant-a");
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        var token = TestTokens.Create("tenant-a", ["Safety"]);
        await using var connection = HubClient.Create(_factory, token);
        var occupancy = new Inbox(connection, EventTypes.OccupancyChanged);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSite", siteId);

        var published = await publisher.PublishAsync("tenant-a", siteId, EventTypes.OccupancyChanged, new { areas = new[] { 1, 2 } }, CancellationToken.None);
        var envelope = await occupancy.NextAsync();

        var response = await _factory.CreateClient().WithToken(token).GetAsync($"/api/v1/events?after={published.Seq - 1}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = doc.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("seq").GetInt64() == published.Seq);

        Assert.Equal(item.GetProperty("seq").GetInt64(), envelope.GetProperty("seq").GetInt64());
        Assert.Equal(item.GetProperty("type").GetString(), envelope.GetProperty("type").GetString());
        Assert.Equal(item.GetProperty("siteId").GetString(), envelope.GetProperty("siteId").GetString());
        Assert.Equal(item.GetProperty("atMs").GetInt64(), envelope.GetProperty("atMs").GetInt64());
        Assert.Equal(item.GetProperty("data").GetRawText(), envelope.GetProperty("data").GetRawText());
    }

    [Fact]
    public async Task JoinSite_OtherTenantUnknownAndInvalidName_GiveSameError()
    {
        var otherTenantSite = await CreateSiteAsync("tenant-b");
        await using var connection = HubClient.Create(_factory, TestTokens.Create("tenant-a", ["Safety"]));
        await connection.StartAsync();

        var messages = new List<string>();
        foreach (var id in new[] { otherTenantSite, $"site-missing-{Guid.NewGuid():N}", "a:b" })
        {
            var ex = await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinSite", id));
            messages.Add(ex.Message);
        }

        Assert.All(messages, m => Assert.Contains("Site not found", m));
        Assert.Equal(1, messages.Distinct().Count());
    }

    [Fact]
    public async Task Negotiate_NoToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/realtime/negotiate?negotiateVersion=1", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_GatewayToken_Returns403()
    {
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", scope: "positions:write"));

        var response = await client.PostAsync("/api/v1/realtime/negotiate?negotiateVersion=1", new StringContent(string.Empty));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task QueryToken_WorksOnHubOnly()
    {
        var siteId = await CreateSiteAsync("tenant-a");
        var token = Uri.EscapeDataString(TestTokens.Create("tenant-a", ["Safety"]));
        var client = _factory.CreateClient(); // no Authorization header

        var negotiate = await client.PostAsync($"/api/v1/realtime/negotiate?negotiateVersion=1&access_token={token}", new StringContent(string.Empty));
        var site = await client.GetAsync($"/api/v1/sites/{siteId}?access_token={token}");

        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, site.StatusCode);
    }

    [Fact]
    public async Task Join_ReceivesNothingForOtherSite()
    {
        var siteOne = await CreateSiteAsync("tenant-a");
        var siteTwo = await CreateSiteAsync("tenant-a");
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        await using var connection = HubClient.Create(_factory, TestTokens.Create("tenant-a", ["Safety"]));
        var occupancy = new Inbox(connection, EventTypes.OccupancyChanged);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSite", siteOne);

        await publisher.PublishAsync("tenant-a", siteTwo, EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);
        var marker = await publisher.PublishAsync("tenant-a", siteOne, EventTypes.OccupancyChanged, new { marker = true }, CancellationToken.None);

        var first = await occupancy.NextAsync();
        Assert.Equal(siteOne, first.GetProperty("siteId").GetString());
        Assert.Equal(marker.Seq, first.GetProperty("seq").GetInt64());
        Assert.Equal(0, occupancy.Count);
    }

    [Fact]
    public async Task LeaveSite_StopsDelivery()
    {
        var siteOne = await CreateSiteAsync("tenant-a");
        var siteTwo = await CreateSiteAsync("tenant-a");
        var publisher = _factory.Services.GetRequiredService<IRealtimePublisher>();
        await using var connection = HubClient.Create(_factory, TestTokens.Create("tenant-a", ["Safety"]));
        var occupancy = new Inbox(connection, EventTypes.OccupancyChanged);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinSite", siteOne);
        await connection.InvokeAsync("LeaveSite", siteOne);

        // Joining site two after the send to site one puts the marker on the same ordered connection.
        await publisher.PublishAsync("tenant-a", siteOne, EventTypes.OccupancyChanged, new { areas = Array.Empty<int>() }, CancellationToken.None);
        await connection.InvokeAsync("JoinSite", siteTwo);
        await publisher.PublishAsync("tenant-a", siteTwo, EventTypes.OccupancyChanged, new { marker = true }, CancellationToken.None);

        var first = await occupancy.NextAsync();
        Assert.Equal(siteTwo, first.GetProperty("siteId").GetString());
        Assert.Equal(0, occupancy.Count);
    }

    private async Task<string> CreateSiteAsync(string tenant)
    {
        var admin = _factory.CreateClient().WithToken(TestTokens.Create(tenant, ["Admin"]));
        var siteId = $"site-{Guid.NewGuid():N}";
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var response = await admin.PostAsync("/api/v1/sites", new StringContent(site.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return siteId;
    }

    // Per-connection order: once the marker arrives, every earlier event for this connection has arrived too.
    private static async Task MarkerAsync(IRealtimePublisher publisher, string tenant, string siteId, Inbox occupancy)
    {
        await publisher.PublishAsync(tenant, siteId, EventTypes.OccupancyChanged, new { marker = true }, CancellationToken.None);
        var marker = await occupancy.NextAsync();
        Assert.True(marker.GetProperty("data").TryGetProperty("marker", out _));
    }

    // Collects one hub method's messages in arrival order; NextAsync waits up to 5 s.
    private sealed class Inbox
    {
        private readonly Queue<JsonElement> _items = new();
        private readonly SemaphoreSlim _signal = new(0);

        public Inbox(HubConnection connection, string method)
        {
            connection.On<JsonElement>(method, e =>
            {
                lock (_items) _items.Enqueue(e);
                _signal.Release();
            });
        }

        public int Count
        {
            get { lock (_items) return _items.Count; }
        }

        public async Task<JsonElement> NextAsync()
        {
            if (!await _signal.WaitAsync(TimeSpan.FromSeconds(5))) throw new TimeoutException("No message within 5 s.");
            lock (_items) return _items.Dequeue();
        }
    }
}
