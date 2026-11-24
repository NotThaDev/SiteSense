using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;

namespace SiteSense.Tests.Api;

public sealed class HealthTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Health_StoreRootWritable_Returns200()
    {
        using var factory = CreateFactory(_root);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void Start_StoreRootMissing_Fails()
    {
        using var factory = CreateFactory("");

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }

    private static WebApplicationFactory<Program> CreateFactory(string rootPath) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("Store:RootPath", rootPath));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
