using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

public sealed class TestModeApiFactory : ApiFactory
{
    public TestModeApiFactory()
    {
    }

    internal TestModeApiFactory(string storeRoot, bool keepStore) : base(storeRoot, keepStore)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("SITESENSE_TEST_MODE", "true");
    }
}

// Api collection: the clock is a static shared by every test that reads it.
[Collection("Api")]
public sealed class TestModeTests : IClassFixture<ApiFactory>, IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268000000;
    private readonly ApiFactory _off;
    private readonly TestModeApiFactory _on;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public TestModeTests(ApiFactory off, TestModeApiFactory on)
    {
        _off = off;
        _on = on;
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    [Theory]
    [InlineData("PUT", "/api/v1/test/clock", null)]
    [InlineData("PUT", "/api/v1/test/clock", "Admin")]
    [InlineData("PUT", "/API/V1/TEST/clock", "Admin")]
    [InlineData("GET", "/api/v1/test/", null)]
    [InlineData("POST", "/api/v1/test/drain", "Admin")]
    [InlineData("POST", "/api/v1/test/jobs/retention/run", "Admin")]
    public async Task Off_AnyTestPath_Returns404Problem(string method, string path, string? role)
    {
        // Arrange
        var client = _off.CreateClient();
        if (role is not null) client.WithToken(TestTokens.Create("tenant-a", [role]));
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = ClockBody(NowMs) };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Off_SimilarPrefix_IsNotGated()
    {
        // Arrange
        var client = _off.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/testing");

        // Assert: the fallback policy answers, not the test-mode gate.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Off_Admin_DoesNotChangeClock()
    {
        // Arrange
        var client = _off.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var before = SiteSenseContext.Time;

        // Act
        await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Assert
        Assert.Same(before, SiteSenseContext.Time);
    }

    [Fact]
    public async Task On_Anonymous_Returns401()
    {
        // Arrange
        var client = _on.CreateClient();

        // Act
        var response = await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    public async Task On_NonAdmin_Returns403(string role)
    {
        // Arrange
        var client = _on.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", [role]));

        // Act
        var response = await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task On_Admin_SetsFrozenClock()
    {
        // Arrange
        var client = _on.CreateClient();
        client.WithToken(TestTokens.Create("tenant-b", ["Admin"]));

        // Act
        var response = await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs));
        var first = SiteSenseContext.Time.GetUtcNow();
        await Task.Delay(20);
        var second = SiteSenseContext.Time.GetUtcNow();

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(NowMs, first.ToUnixTimeMilliseconds());
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task On_SecondPut_ReplacesClock()
    {
        // Arrange
        var client = _on.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        await client.PutAsync("/api/v1/test/clock", ClockBody(NowMs + 600_000));

        // Assert
        Assert.Equal(NowMs + 600_000, SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("{\"nowMs\":\"soon\"}")]
    [InlineData("{\"nowMs\":253402300800000}")]
    [InlineData("{\"nowMs\":-62135596800001}")]
    public async Task On_InvalidBody_Returns400(string body)
    {
        // Arrange
        var client = _on.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var before = SiteSenseContext.Time;

        // Act
        var response = await client.PutAsync("/api/v1/test/clock", new StringContent(body, Encoding.UTF8, "application/json"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Same(before, SiteSenseContext.Time);
    }

    private static HttpContent ClockBody(long nowMs) => JsonContent.Create(new { nowMs });
}
