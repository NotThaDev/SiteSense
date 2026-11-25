using SiteSense.Api.Helpers;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models.Map;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Maps;

[Collection("Api")]
public sealed class SiteMapImportTests : IDisposable
{
    private const double Tolerance = 0.5;
    private const string Tenant = "tenant-a";
    private const string Site = "site-1";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
    private readonly IDocumentStore _previous;

    public SiteMapImportTests()
    {
        _previous = SiteSenseContext.Store;
        SiteSenseContext.Store = new FileDocumentStore(_root);
    }

    public void Dispose()
    {
        SiteSenseContext.Store = _previous;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task ValidMap_IsSavedAsVersion1()
    {
        var result = await Import(MapFixtures.BaseMap, 0);

        Assert.Empty(result.Errors);
        Assert.Equal(1L, result.MapVersion);
        var stored = await SiteMap.GetAsync(Tenant, Site, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(2, stored.Areas.Count);
    }

    [Fact]
    public async Task SecondImportWithCurrentVersion_IsVersion2()
    {
        await Import(MapFixtures.BaseMap, 0);

        var result = await Import(MapFixtures.BaseMap, 1);

        Assert.Equal(2L, result.MapVersion);
    }

    [Fact]
    public async Task StaleExpectedVersion_Throws()
    {
        await Import(MapFixtures.BaseMap, 0);
        await Import(MapFixtures.BaseMap, 1);

        await Assert.ThrowsAsync<ConcurrencyException>(() => Import(MapFixtures.BaseMap, 1));
    }

    [Fact]
    public async Task InvalidMap_IsNotSaved()
    {
        await Import(MapFixtures.BaseMap, 0);
        await Import(MapFixtures.BaseMap, 1);
        var invalid = MapFixtures.BaseMap.Replace("\"areaType\": \"perimeter\"", "\"areaType\": \"work\"");

        var result = await Import(invalid, 2);

        Assert.NotEmpty(result.Errors);
        Assert.Null(result.MapVersion);
        var stored = await SiteMap.GetAsync(Tenant, Site, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(2L, stored.Version);
    }

    private static Task<MapImportResult> Import(string geoJson, long expectedVersion) =>
        SiteMap.ImportAsync(Tenant, Site, geoJson, expectedVersion, Tolerance, CancellationToken.None);
}
