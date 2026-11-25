using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storeRoot = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Store:RootPath", _storeRoot);
        builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
        builder.UseSetting("Auth:Audience", TestTokens.Audience);
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ErrorProbeController).Assembly);
            // Use Configure, not PostConfigure: the static configuration must exist before the handler's own post-configure runs, otherwise it calls the network.
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
            {
                // Static keys: no call to Keycloak in tests.
                var config = new OpenIdConnectConfiguration { Issuer = TestTokens.Issuer };
                config.SigningKeys.Add(TestTokens.Key);
                o.Configuration = config;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storeRoot)) Directory.Delete(_storeRoot, recursive: true);
    }
}
