using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SiteSense.Api.Auth;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Realtime;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Jobs;

// Api collection: the clock is a static shared by every test that reads it.
[Collection("Api")]
public sealed class OccupancySiteJobTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268000000;
    private const string SiteId = "site-occ-job";
    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public OccupancySiteJobTests(TestModeApiFactory factory)
    {
        _factory = factory;
        _ = _factory.Services;
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    [Fact]
    public async Task Run_DeviceWentSilent_PublishesZeroOnceThenNothing()
    {
        // Arrange
        var admin = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await SiteSenseContext.Store.SaveAsync("tenant-a", new Site
        {
            Id = SiteId, TenantId = "tenant-a", Name = "Test site", TimeZone = "Europe/Rome",
            Navigation = new NavigationSettings(1.3, 0.8, 0.5, 0.5, 30),
        }, CancellationToken.None);
        var ring = new[] { new[] { new[] { 9.1, 45.4 }, new[] { 9.3, 45.4 }, new[] { 9.3, 45.6 }, new[] { 9.1, 45.6 } } };
        await SiteSenseContext.Store.SaveAsync("tenant-a", new SiteMap { Id = SiteId, TenantId = "tenant-a", Areas = [new("perimeter", "Site", "perimeter", null, ring)] }, CancellationToken.None);
        await Device.CreateAsync("tenant-a", new Device { Id = "d1", TenantId = "tenant-a", SiteId = SiteId }, CancellationToken.None);
        await TrackPoint.AppendAsync("tenant-a", "d1", [new TrackPoint("d1", NowMs - 60_000, 45.5, 9.2, 4.5, 0, false, "perimeter")], CancellationToken.None);
        await admin.PutAsync("/api/v1/test/clock", JsonContent.Create(new { nowMs = NowMs }));
        await Run(admin);
        Assert.Equal([1], await CountsAsync());

        // Act
        await admin.PutAsync("/api/v1/test/clock", JsonContent.Create(new { nowMs = NowMs + 6 * 60_000 }));
        await Run(admin);
        await Run(admin);

        // Assert
        Assert.Equal([1, 0], await CountsAsync());
    }

    private static async Task Run(HttpClient admin) =>
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/v1/test/jobs/occupancy/run", null)).StatusCode);

    // The perimeter count of every OccupancyChanged this site published, in order.
    private async Task<List<int>> CountsAsync()
    {
        var page = await _factory.Services.GetRequiredService<EventPublisher>()
            .ReadAfterAsync("tenant-a", 0, [SiteSensePolicies.CanSeeAggregates], CancellationToken.None);
        return page.Items.Where(e => e.Type == EventTypes.OccupancyChanged && e.SiteId == SiteId)
            .Select(e => e.Data.GetProperty("areas")[0].GetProperty("count").GetInt32()).ToList();
    }
}
