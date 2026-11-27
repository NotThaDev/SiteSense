using System.Net;
using System.Net.Http.Json;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Store;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Jobs;

// Api collection: the clock is a static shared by every test that reads it.
[Collection("Api")]
public sealed class RetentionJobTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268045123;
    private const long DayMs = 86_400_000;
    private const string RetentionRun = "/api/v1/test/jobs/retention/run";

    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public RetentionJobTests(TestModeApiFactory factory)
    {
        _factory = factory;
        // Build the host first: its startup sets the static clock.
        _ = _factory.Services;
        SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(NowMs));
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    [Fact]
    public async Task RunRetention_DeletesExpiredPositions_KeepsRecent()
    {
        // Arrange
        // Test RetentionDays is 30 (ApiFactory): 31 days old is expired, 1 day old is kept.
        var admin = AdminClient("tenant-a");
        var device = "ret-1-keep";
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new Position(device, NowMs - 31 * DayMs, 45.5, 9.2, 4.5) }, CancellationToken.None);
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new Position(device, NowMs - 1 * DayMs, 45.5, 9.2, 4.5) }, CancellationToken.None);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var response = await admin.PostAsync(RetentionRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var left = await SiteSenseContext.Log.ReadRangeAsync<Position>("tenant-a", device, NowMs - 40 * DayMs, NowMs + 1, CancellationToken.None);
        var only = Assert.Single(left);
        Assert.Equal(NowMs - 1 * DayMs, only.TimestampMs);
    }

    [Fact]
    public async Task RunRetention_DeletesExpiredTrackPoints_KeepsRecent()
    {
        // Arrange
        // Test RetentionDays is 30 (ApiFactory): 31 days old is expired, 1 day old is kept.
        var admin = AdminClient("tenant-a");
        var device = "ret-track-keep";
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new TrackPoint(device, NowMs - 31 * DayMs, 45.5, 9.2, 4.5, 0) }, CancellationToken.None);
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new TrackPoint(device, NowMs - 1 * DayMs, 45.5, 9.2, 4.5, 0) }, CancellationToken.None);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var response = await admin.PostAsync(RetentionRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var left = await TrackPoint.ReadAsync("tenant-a", device, NowMs - 40 * DayMs, NowMs + 1, CancellationToken.None);
        var only = Assert.Single(left);
        Assert.Equal(NowMs - 1 * DayMs, only.TimestampMs);
    }

    [Fact]
    public async Task RunRetention_Twice_SameResult()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var device = "ret-2-twice";
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new Position(device, NowMs - 31 * DayMs, 45.5, 9.2, 4.5) }, CancellationToken.None);
        await SiteSenseContext.Log.AppendAsync("tenant-a", device, new[] { new Position(device, NowMs - 1 * DayMs, 45.5, 9.2, 4.5) }, CancellationToken.None);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var first = await admin.PostAsync(RetentionRun, null);
        var afterFirst = await SiteSenseContext.Log.ReadRangeAsync<Position>("tenant-a", device, NowMs - 40 * DayMs, NowMs + 1, CancellationToken.None);
        var second = await admin.PostAsync(RetentionRun, null);
        var afterSecond = await SiteSenseContext.Log.ReadRangeAsync<Position>("tenant-a", device, NowMs - 40 * DayMs, NowMs + 1, CancellationToken.None);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task RunRetention_OnlyCallersTenant()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var device = "ret-3-other";
        await SiteSenseContext.Log.AppendAsync("tenant-b", device, new[] { new Position(device, NowMs - 31 * DayMs, 45.5, 9.2, 4.5) }, CancellationToken.None);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var response = await admin.PostAsync(RetentionRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var kept = await SiteSenseContext.Log.ReadRangeAsync<Position>("tenant-b", device, NowMs - 40 * DayMs, NowMs + 1, CancellationToken.None);
        Assert.Single(kept);
    }

    [Fact]
    public async Task RunJob_UnknownName_Still404()
    {
        // Arrange
        var admin = AdminClient("tenant-a");

        // Act
        var response = await admin.PostAsync("/api/v1/test/jobs/no-such-job/run", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient AdminClient(string tenant)
    {
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, ["Admin"]));
        return client;
    }

    private static HttpContent ClockBody(long nowMs) => JsonContent.Create(new { nowMs });
}
