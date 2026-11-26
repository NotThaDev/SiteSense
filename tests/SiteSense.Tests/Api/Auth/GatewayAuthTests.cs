using System.Net;
using System.Text;
using System.Text.Json;

namespace SiteSense.Tests.Api.Auth;

[Collection("Api")]
public sealed class GatewayAuthTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public GatewayAuthTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("tenant-a")]
    [InlineData("tenant-b")]
    public async Task PostPositions_GatewayToken_PassesGuard(string tenant)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, scope: "positions:write"));

        // Act
        var response = await client.PostAsync("/api/v1/positions", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostPositions_UserRoleWithScope_PassesGuard()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"], scope: "positions:write"));

        // Act
        var response = await client.PostAsync("/api/v1/positions", new StringContent("{}", Encoding.UTF8, "application/json"));

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Safety")]
    [InlineData("Viewer")]
    public async Task PostPositions_UserToken_Returns403(string role)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", [role]));

        // Act
        var response = await client.PostAsync("/api/v1/positions", null);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostPositions_GatewayTokenWithoutScope_Returns403()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a"));

        // Act
        var response = await client.PostAsync("/api/v1/positions", null);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostPositions_NoToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsync("/api/v1/positions", null);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal((int)expected, body.RootElement.GetProperty("status").GetInt32());
    }
}
