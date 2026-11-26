using System.Net;
using Microsoft.AspNetCore.Hosting;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

[Collection("Api")]
public sealed class StaticFilesTests : IClassFixture<ApiFactory>, IDisposable
{
    private readonly ApiFactory _factory;
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    public StaticFilesTests(ApiFactory factory)
    {
        _factory = factory;
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><title>stub</title>");
        Directory.CreateDirectory(Path.Combine(_webRoot, "assets"));
        File.WriteAllText(Path.Combine(_webRoot, "assets", "app.js"), "console.log('stub');");
    }

    [Fact]
    public async Task Asset_ReturnsTheFileNotIndexHtml()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot)).CreateClient();

        // Act
        var response = await client.GetAsync("/assets/app.js");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("console.log", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownApiPath_Returns404ProblemNotHtml()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot)).CreateClient()
            .WithToken(TestTokens.Create("tenant-a", ["Admin"]));

        // Act
        var response = await client.GetAsync("/api/v1/nope");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sites/site-01")]
    public async Task UiPath_WithoutLogin_ReturnsIndexHtml(string path)
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(b => b.UseWebRoot(_webRoot)).CreateClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("stub", await response.Content.ReadAsStringAsync());
    }

    public void Dispose() => Directory.Delete(_webRoot, recursive: true);
}
