using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

// Coordinates are WGS84 [lon, lat], as received.
public sealed record MapArea(string Id, string Name, string AreaType, int? Capacity, double[][][] Coordinates);
public sealed record MapPath(string Id, string Surface, bool Steps, double SlopePercent, double WidthM, double[][] Coordinates);
public sealed record MapAssemblyPoint(string Id, string Name, int Capacity, double[] Coordinates);
public sealed record MapEntrance(string Id, string Name, double[] Coordinates);
public sealed record MapImportResult(long? MapVersion, IReadOnlyList<MapError> Errors);

public sealed class SiteMap : IDocument
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public long Version { get; set; }
    public List<MapArea> Areas { get; set; } = [];
    public List<MapPath> Paths { get; set; } = [];
    public List<MapAssemblyPoint> AssemblyPoints { get; set; } = [];
    public List<MapEntrance> Entrances { get; set; } = [];

    public static Task<SiteMap?> GetAsync(string tenantId, string siteId, CancellationToken ct) =>
        SiteSenseContext.Store.GetAsync<SiteMap>(tenantId, siteId, ct);

    // expectedVersion 0 creates the map; any other value must be the stored version, else ConcurrencyException.
    public static async Task<MapImportResult> ImportAsync(
        string tenantId, string siteId, string geoJson, long expectedVersion, double connectToleranceM, CancellationToken ct)
    {
        var (map, errors) = MapValidator.Validate(geoJson, connectToleranceM);
        if (map is null || errors.Count > 0) return new MapImportResult(null, errors);

        map.Id = siteId;
        map.TenantId = tenantId;
        map.Version = expectedVersion;
        var saved = await SiteSenseContext.Store.SaveAsync(tenantId, map, ct);
        return new MapImportResult(saved.Version, []);
    }
}
