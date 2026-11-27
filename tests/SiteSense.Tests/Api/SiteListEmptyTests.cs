using System.Net;
using System.Text.Json;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

// Own class, so its own fixture gives an empty store without re-pointing the static store mid-class.
[Collection("Api")]
public sealed class SiteListEmptyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task List_EmptyTenant_ReturnsEmpty()
    {
        // Arrange
        var client = factory.CreateClient().WithToken(TestTokens.Create("tenant-b", ["Admin"]));

        // Act
        var response = await client.GetAsync("/api/v1/sites");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
    }
}
