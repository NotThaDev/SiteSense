using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using SiteSense.Api.Alarms;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Alarms;

[Collection("Api")]
public sealed class AlertsEndpointTests : IClassFixture<ApiFactory>
{
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    private readonly ApiFactory _factory;

    public AlertsEndpointTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_NewestFirst_ReturnsFieldsOfEachAlert()
    {
        var safety = Client("tenant-a", "Safety");
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));
        await NotifyAsync(site, AlertTypes.AreaIntrusion, "dev-a", "area-1", 1000, late: false);
        await NotifyAsync(site, AlertTypes.DeviceSilent, "dev-b", null, 3000, late: true);

        var body = await GetListAsync(safety, site, "");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new long[] { 3000, 1000 }, items.Select(i => i.GetProperty("eventTimeMs").GetInt64()).ToArray());
        var first = items[0];
        Assert.Equal(site, first.GetProperty("siteId").GetString());
        Assert.Equal(AlertTypes.DeviceSilent, first.GetProperty("type").GetString());
        Assert.Equal("dev-b", first.GetProperty("deviceId").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("areaId").ValueKind);
        Assert.True(first.GetProperty("late").GetBoolean());
        Assert.Equal(AlertStates.Active, first.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, first.GetProperty("endTimeMs").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task List_FromToFilter_IsInclusive()
    {
        var safety = Client("tenant-a", "Safety");
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));
        foreach (var t in new long[] { 1000, 2000, 3000, 4000 })
            await NotifyAsync(site, AlertTypes.AreaIntrusion, "dev-f", "area-1", t, late: false);

        var body = await GetListAsync(safety, site, "fromMs=2000&toMs=3000");

        Assert.Equal(new long[] { 3000, 2000 }, EventTimes(body));
    }

    [Fact]
    public async Task List_TypeAndStateFilters()
    {
        var safety = Client("tenant-a", "Safety");
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));
        await NotifyAsync(site, AlertTypes.AreaIntrusion, "dev-t", "area-1", 1000, late: false);
        await NotifyAsync(site, AlertTypes.DeviceSilent, "dev-t", null, 2000, late: false);
        await NotifyAsync(site, AlertTypes.AreaIntrusion, "dev-t", "area-2", 3000, late: false);
        await Alarms().DeactivateAlarmAsync("tenant-a", Alert.IdFor(AlertTypes.AreaIntrusion, site, "dev-t", "area-1", 1000), 1500, CancellationToken.None);

        var byType = await GetListAsync(safety, site, "type=AreaIntrusion");
        var byState = await GetListAsync(safety, site, "state=NotActive");

        Assert.Equal(new long[] { 3000, 1000 }, EventTimes(byType));
        Assert.Equal(new long[] { 1000 }, EventTimes(byState));
    }

    [Fact]
    public async Task List_CursorPaging_ReturnsEveryAlertOnce()
    {
        var safety = Client("tenant-a", "Safety");
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));
        for (var i = 1; i <= 5; i++)
            await NotifyAsync(site, AlertTypes.AreaIntrusion, $"dev-p{i}", "area-1", i * 1000, late: false);

        var seen = new List<long>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var body = await GetListAsync(safety, site, $"limit=2{(cursor is null ? "" : "&cursor=" + cursor)}");
            seen.AddRange(EventTimes(body));
            cursor = body.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : body.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(new long[] { 5000, 4000, 3000, 2000, 1000 }, seen);
    }

    [Fact]
    public async Task List_OtherTenantsSite_Returns404()
    {
        var adminA = Client("tenant-a", "Admin");
        var site = await CreateSiteAsync(adminA);

        var response = await Client("tenant-b", "Safety").GetAsync($"/api/v1/sites/{site}/alerts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task List_Viewer_Returns403()
    {
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));

        var response = await Client("tenant-a", "Viewer").GetAsync($"/api/v1/sites/{site}/alerts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("cursor=not-base64!")]
    [InlineData("limit=501")]
    [InlineData("limit=0")]
    [InlineData("state=Closed")]
    [InlineData("type=Unknown")]
    public async Task List_InvalidQuery_Returns400(string query)
    {
        var safety = Client("tenant-a", "Safety");
        var site = await CreateSiteAsync(Client("tenant-a", "Admin"));

        var response = await safety.GetAsync($"/api/v1/sites/{site}/alerts?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task List_UnknownSite_Returns404()
    {
        var response = await Client("tenant-a", "Safety").GetAsync("/api/v1/sites/no-such-site/alerts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListTenant_TwoSitesOfTenantA_NoneOfTenantB()
    {
        var admin = Client("tenant-a", "Admin");
        var siteOne = await CreateSiteAsync(admin);
        var siteTwo = await CreateSiteAsync(admin);
        var otherSite = await CreateSiteAsync(Client("tenant-b", "Admin"));
        await NotifyAsync(siteOne, AlertTypes.AreaIntrusion, "dev-w1", "area-1", 1000, late: false);
        await NotifyAsync(siteTwo, AlertTypes.DeviceSilent, "dev-w2", null, 2000, late: false);
        await Alarms().NotifyAlarmAsync("tenant-b", otherSite, AlertTypes.AreaIntrusion, "dev-w3", "area-1", 3000, false, CancellationToken.None);

        var body = await GetTenantListAsync(Client("tenant-a", "Safety"), "limit=500");

        var items = body.GetProperty("items").EnumerateArray().ToList();
        var sites = items.Select(i => i.GetProperty("siteId").GetString()).ToList();
        Assert.Contains(siteOne, sites);
        Assert.Contains(siteTwo, sites);
        Assert.DoesNotContain(otherSite, sites);
        var times = items.Select(i => i.GetProperty("eventTimeMs").GetInt64()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
    }

    [Fact]
    public async Task ListTenant_FiltersAndPaging()
    {
        var admin = Client("tenant-a", "Admin");
        var siteOne = await CreateSiteAsync(admin);
        var siteTwo = await CreateSiteAsync(admin);
        // Far-future times keep these on top of whatever other tests left for tenant-a.
        const long Base = 9_000_000_000_000;
        await NotifyAsync(siteOne, AlertTypes.AreaIntrusion, "dev-q1", "area-1", Base + 1000, late: false);
        await NotifyAsync(siteTwo, AlertTypes.DeviceSilent, "dev-q2", null, Base + 2000, late: false);
        await NotifyAsync(siteOne, AlertTypes.AreaIntrusion, "dev-q3", "area-2", Base + 3000, late: false);
        await Alarms().DeactivateAlarmAsync("tenant-a", Alert.IdFor(AlertTypes.AreaIntrusion, siteOne, "dev-q1", "area-1", Base + 1000), Base + 1500, CancellationToken.None);
        var safety = Client("tenant-a", "Safety");

        var range = await GetTenantListAsync(safety, $"fromMs={Base + 2000}&toMs={Base + 3000}");
        var byType = await GetTenantListAsync(safety, $"fromMs={Base}&type=AreaIntrusion");
        var byState = await GetTenantListAsync(safety, $"fromMs={Base}&state=NotActive");
        var seen = new List<long>();
        string? cursor = null;
        for (var pages = 0; pages < 10; pages++)
        {
            var body = await GetTenantListAsync(safety, $"fromMs={Base}&limit=2{(cursor is null ? "" : "&cursor=" + cursor)}");
            seen.AddRange(EventTimes(body));
            cursor = body.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : body.GetProperty("nextCursor").GetString();
            if (cursor is null) break;
        }

        Assert.Equal(new[] { Base + 3000, Base + 2000 }, EventTimes(range));
        Assert.Equal(new[] { Base + 3000, Base + 1000 }, EventTimes(byType));
        Assert.Equal(new[] { Base + 1000 }, EventTimes(byState));
        Assert.Equal(new[] { Base + 3000, Base + 2000, Base + 1000 }, seen);
    }

    [Fact]
    public async Task ListTenant_Viewer_Returns403()
    {
        var response = await Client("tenant-a", "Viewer").GetAsync("/api/v1/alerts");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient Client(string tenant, string role) =>
        _factory.CreateClient().WithToken(TestTokens.Create(tenant, [role]));

    private AlarmManager Alarms() => _factory.Services.GetRequiredService<AlarmManager>();

    private Task NotifyAsync(string site, string type, string deviceId, string? areaId, long eventTimeMs, bool late) =>
        Alarms().NotifyAlarmAsync("tenant-a", site, type, deviceId, areaId, eventTimeMs, late, CancellationToken.None);

    private static async Task<JsonElement> GetListAsync(HttpClient client, string site, string query)
    {
        var response = await client.GetAsync($"/api/v1/sites/{site}/alerts?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> GetTenantListAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/alerts?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    private static long[] EventTimes(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("eventTimeMs").GetInt64()).ToArray();

    private static async Task<string> CreateSiteAsync(HttpClient admin)
    {
        var siteId = $"site-{Guid.NewGuid():N}";
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var response = await admin.PostAsync("/api/v1/sites", new StringContent(site.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return siteId;
    }
}
