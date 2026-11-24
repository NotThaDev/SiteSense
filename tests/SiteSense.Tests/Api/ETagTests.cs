using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class ETagTests : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    private readonly HttpClient _client;

    public ETagTests(ApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Get_ReturnsETag()
    {
        // Arrange
        const string id = "get-1";
        await SeedAsync(id);

        // Act
        var response = await _client.GetAsync($"/probe/docs/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_MatchingIfMatch_Returns200WithNextETag()
    {
        // Arrange
        const string id = "put-ok";
        await SeedAsync(id);

        // Act
        var response = await PutAsync(id, "\"1\"", "new");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task Put_StaleIfMatch_Returns412Problem()
    {
        // Arrange
        const string id = "put-stale";
        await SeedAsync(id);
        var first = await PutAsync(id, "\"1\"", "second");

        // Act
        var response = await PutAsync(id, "\"1\"", "stale");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("\"status\":412", body);
        Assert.DoesNotContain(id, body);
    }

    [Fact]
    public async Task Put_MissingIfMatch_Returns412Problem()
    {
        // Arrange
        const string id = "put-missing";
        await SeedAsync(id);

        // Act
        var response = await PutAsync(id, null, "new");

        // Assert
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    private static async Task SeedAsync(string id)
    {
        await SiteSenseContext.Store.SaveAsync("tenant-a", new TestDoc { Id = id, TenantId = "tenant-a", Name = "start" }, CancellationToken.None);
    }

    private Task<HttpResponseMessage> PutAsync(string id, string? ifMatch, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/probe/docs/{id}") { Content = JsonContent.Create(new { name }) };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return _client.SendAsync(req);
    }
}
