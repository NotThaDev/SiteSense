using SiteSense.Api.Tenants;

namespace SiteSense.Tests.Api;

public sealed class TenantOptionsValidatorTests
{
    [Fact]
    public void Validate_NoTenants_Fails()
    {
        var result = new TenantOptionsValidator().Validate(null, new Dictionary<string, TenantSettings>());

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_InvalidId_Fails()
    {
        var tenants = Valid();
        tenants["bad id"] = new TenantSettings { RetentionDays = 30, SilentAfterMinutes = 10 };

        var result = new TenantOptionsValidator().Validate(null, tenants);

        Assert.True(result.Failed);
        Assert.Contains("bad id", result.FailureMessage);
    }

    [Fact]
    public void Validate_ZeroRetentionDays_Fails()
    {
        var tenants = Valid();
        tenants["tenant-a"].RetentionDays = 0;

        var result = new TenantOptionsValidator().Validate(null, tenants);

        Assert.True(result.Failed);
        Assert.Contains("Tenants:tenant-a:RetentionDays", result.FailureMessage);
    }

    [Fact]
    public void Validate_ZeroSilentAfterMinutes_Fails()
    {
        var tenants = Valid();
        tenants["tenant-b"].SilentAfterMinutes = 0;

        var result = new TenantOptionsValidator().Validate(null, tenants);

        Assert.True(result.Failed);
        Assert.Contains("Tenants:tenant-b:SilentAfterMinutes", result.FailureMessage);
    }

    [Fact]
    public void Validate_TwoValidTenants_Succeeds()
    {
        var result = new TenantOptionsValidator().Validate(null, Valid());

        Assert.True(result.Succeeded);
    }

    private static Dictionary<string, TenantSettings> Valid() => new()
    {
        ["tenant-a"] = new() { RetentionDays = 30, SilentAfterMinutes = 10 },
        ["tenant-b"] = new() { RetentionDays = 30, SilentAfterMinutes = 10 },
    };
}
