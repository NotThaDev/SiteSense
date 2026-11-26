using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _storeRoot = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    public string StoreRoot => _storeRoot;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Store:RootPath", _storeRoot);
        builder.UseSetting("Tenants:tenant-a:RetentionDays", "30");
        builder.UseSetting("Tenants:tenant-a:SilentAfterMinutes", "10");
        builder.UseSetting("Tenants:tenant-b:RetentionDays", "30");
        builder.UseSetting("Tenants:tenant-b:SilentAfterMinutes", "10");
        builder.UseSetting("Auth:Issuer", TestTokens.Issuer);
        builder.UseSetting("Auth:Audience", TestTokens.Audience);
        // The OIDC handler checks this on every request, and the test issuer is plain HTTP.
        builder.UseSetting("Auth:RequireHttpsMetadata", "false");
        // Pinned so a developer or CI environment variable cannot turn test mode on.
        builder.UseSetting("SITESENSE_TEST_MODE", "false");
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
            // Static metadata for the browser login: no call to Keycloak in tests.
            services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, o =>
                o.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = TestTokens.Issuer,
                    AuthorizationEndpoint = TestTokens.Issuer + "/protocol/openid-connect/auth",
                    EndSessionEndpoint = TestTokens.Issuer + "/protocol/openid-connect/logout",
                });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_storeRoot)) Directory.Delete(_storeRoot, recursive: true);
    }
}
