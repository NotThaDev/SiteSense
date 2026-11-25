using System.Net;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api.Auth;

[Collection("Api")]
public sealed class TenantIsolationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public TenantIsolationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_OtherTenantsDocument_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();
        // Creating the host resets the static store, so seed after CreateClient.
        await SiteSenseContext.Store.SaveAsync("tenant-b", new TestDoc { Id = "iso-doc", TenantId = "tenant-b", Name = "b" }, CancellationToken.None);
        client.WithToken(TestTokens.Create("tenant-a", ["Admin"]));

        // Act
        var response = await client.GetAsync("/probe/tenant-docs/iso-doc");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_OwnTenantsDocument_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();
        await SiteSenseContext.Store.SaveAsync("tenant-b", new TestDoc { Id = "iso-doc-own", TenantId = "tenant-b", Name = "b" }, CancellationToken.None);
        client.WithToken(TestTokens.Create("tenant-b", ["Admin"]));

        // Act
        var response = await client.GetAsync("/probe/tenant-docs/iso-doc-own");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
