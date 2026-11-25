using Microsoft.AspNetCore.Hosting;
using SiteSense.Api.Helpers;

namespace SiteSense.Tests.Api;

// Builds hosts, which reset the static store: must not run in parallel with other host tests.
[Collection("Api")]
public sealed class TenantStartupTests
{
    [Fact]
    public void Startup_ValidConfig_BindsBothTenants()
    {
        using var factory = new ApiFactory();
        factory.CreateClient();

        Assert.Equal(30, SiteSenseContext.Tenants["tenant-a"].RetentionDays);
        Assert.Equal(10, SiteSenseContext.Tenants["tenant-b"].SilentAfterMinutes);
        Assert.False(SiteSenseContext.Tenants.ContainsKey("tenant-x"));
    }

    [Fact]
    public void Startup_ZeroRetentionDays_Throws()
    {
        using var baseFactory = new ApiFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.UseSetting("Tenants:tenant-a:RetentionDays", "0"));

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Tenants:tenant-a:RetentionDays", ex.ToString());
    }
}
