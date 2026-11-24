using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace SiteSense.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storeRoot = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Store:RootPath", _storeRoot);
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ErrorProbeController).Assembly));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storeRoot)) Directory.Delete(_storeRoot, recursive: true);
    }
}
