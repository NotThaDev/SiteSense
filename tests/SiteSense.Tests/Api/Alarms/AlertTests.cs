using SiteSense.Api.Alarms;

namespace SiteSense.Tests.Api.Alarms;

[Collection("Api")]
public sealed class AlertTests : IClassFixture<ApiFactory>
{
    private const string Tenant = "tenant-a";

    public AlertTests(ApiFactory factory)
    {
        // Starting the host sets the store that Alert saves to.
        _ = factory.Services;
    }

    [Fact]
    public void IdFor_SameInputs_SameLowercaseHex()
    {
        var first = Alert.IdFor(AlertTypes.AreaIntrusion, "site-1", "dev-1", "area-1", 1000);
        var second = Alert.IdFor(AlertTypes.AreaIntrusion, "site-1", "dev-1", "area-1", 1000);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Theory]
    [InlineData(AlertTypes.ZoneAnomaly, "site-1", "dev-1", "area-1", 1000)]
    [InlineData(AlertTypes.AreaIntrusion, "site-2", "dev-1", "area-1", 1000)]
    [InlineData(AlertTypes.AreaIntrusion, "site-1", "dev-2", "area-1", 1000)]
    [InlineData(AlertTypes.AreaIntrusion, "site-1", "dev-1", "area-2", 1000)]
    [InlineData(AlertTypes.AreaIntrusion, "site-1", "dev-1", "area-1", 1001)]
    [InlineData(AlertTypes.AreaIntrusion, "site-1", null, "area-1", 1000)]
    public void IdFor_AnyInputChanged_DifferentId(string type, string siteId, string? deviceId, string? areaId, long eventTimeMs)
    {
        var baseline = Alert.IdFor(AlertTypes.AreaIntrusion, "site-1", "dev-1", "area-1", 1000);

        Assert.NotEqual(baseline, Alert.IdFor(type, siteId, deviceId, areaId, eventTimeMs));
    }

    [Fact]
    public async Task CreateAsync_SameId_SecondReturnsNull()
    {
        var id = Alert.IdFor(AlertTypes.DeviceSilent, "site-create", "dev-create", null, 5000);
        var alert = () => new Alert
        {
            Id = id, TenantId = Tenant, SiteId = "site-create", Type = AlertTypes.DeviceSilent,
            EventTimeMs = 5000, DeviceId = "dev-create", Late = false, State = AlertStates.Active,
        };

        var first = await Alert.CreateAsync(Tenant, alert(), CancellationToken.None);
        var second = await Alert.CreateAsync(Tenant, alert(), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
    }
}
