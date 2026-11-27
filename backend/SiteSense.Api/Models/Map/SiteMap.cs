using System.Text.Json.Nodes;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;
using SiteSense.Domain.Geometry;
using SiteSense.Domain.Navigation;

namespace SiteSense.Api.Models.Map;

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

    // RFC 7946 FeatureCollection with the import's own keys; a JsonObject keeps them exact (no naming policy).
    public JsonObject ToGeoJson()
    {
        var features = new JsonArray();
        foreach (var a in Areas)
        {
            var props = Props("area", a.Id, ("name", a.Name), ("areaType", a.AreaType));
            if (a.Capacity is not null) props["capacity"] = a.Capacity;
            features.Add(Feature(new JsonObject { ["type"] = "Polygon", ["coordinates"] = Json(a.Coordinates) }, props));
        }
        foreach (var p in Paths)
            features.Add(Feature(new JsonObject { ["type"] = "LineString", ["coordinates"] = Json(p.Coordinates) },
                Props("path", p.Id, ("surface", p.Surface), ("steps", p.Steps), ("slopePercent", p.SlopePercent), ("widthM", p.WidthM))));
        foreach (var ap in AssemblyPoints)
            features.Add(Feature(new JsonObject { ["type"] = "Point", ["coordinates"] = Json(ap.Coordinates) },
                Props("assemblyPoint", ap.Id, ("name", ap.Name), ("capacity", ap.Capacity))));
        foreach (var e in Entrances)
            features.Add(Feature(new JsonObject { ["type"] = "Point", ["coordinates"] = Json(e.Coordinates) },
                Props("entrance", e.Id, ("name", e.Name))));
        return new JsonObject { ["type"] = "FeatureCollection", ["features"] = features };
    }

    private static JsonObject Feature(JsonObject geometry, JsonObject props) =>
        new() { ["type"] = "Feature", ["geometry"] = geometry, ["properties"] = props };

    private static JsonObject Props(string kind, string id, params (string Key, object Value)[] rest)
    {
        var o = new JsonObject { ["kind"] = kind, ["id"] = id };
        foreach (var (k, v) in rest) o[k] = JsonValue.Create(v);
        return o;
    }

    private static JsonNode Json(Array coordinates) =>
        coordinates is double[] pos
            ? new JsonArray(pos.Select(c => (JsonNode)JsonValue.Create(c)).ToArray())
            : new JsonArray(coordinates.Cast<Array>().Select(Json).ToArray());

    // expectedVersion null = replace the latest map (last write wins); a number must be the stored version (0 = new), else ConcurrencyException.
    public static async Task<MapImportResult> ImportAsync(
        string tenantId, string siteId, string geoJson, long? expectedVersion, double connectToleranceM, CancellationToken ct)
    {
        var (map, errors) = MapValidator.Validate(geoJson, connectToleranceM);
        if (map is null || errors.Count > 0) return new MapImportResult(null, errors);

        map.Id = siteId;
        map.TenantId = tenantId;
        if (expectedVersion is not null)
        {
            map.Version = expectedVersion.Value;
            var saved = await SiteSenseContext.Store.SaveAsync(tenantId, map, ct);
            return new MapImportResult(saved.Version, []);
        }

        for (var attempt = 0; attempt < MaxLatestAttempts; attempt++)
        {
            map.Version = (await GetAsync(tenantId, siteId, ct))?.Version ?? 0;
            try
            {
                var saved = await SiteSenseContext.Store.SaveAsync(tenantId, map, ct);
                return new MapImportResult(saved.Version, []);
            }
            catch (ConcurrencyException)
            {
                // A concurrent upload won: read the new version and try again.
            }
        }
        throw new MapUploadConflictException();
    }

    // Local metres from the perimeter centroid: the same origin MapValidator uses for its tolerance check (MAP-02).
    public RouteGraph BuildRouteGraph(NavigationSettings nav) => BuildRouteContext(nav).Graph;

    // The graph and the projection it was built with: one projection, so route points convert back exactly.
    public (RouteGraph Graph, LocalProjection Projection) BuildRouteContext(NavigationSettings nav)
    {
        var perimeter = Areas.Single(a => a.AreaType == "perimeter").Coordinates[0];
        var projection = LocalProjection.FromCentroid(perimeter.Select(p => (Lon: p[0], Lat: p[1])).ToList());
        IReadOnlyList<(double X, double Y)> Local(double[][] line) => line.Select(p => projection.ToLocal(p[0], p[1])).ToList();

        var paths = Paths.Select(p => new GraphPath(p.Id, p.Surface == "unpaved", p.Steps, Local(p.Coordinates))).ToList();
        var blocked = Areas
            .Where(a => a.AreaType is "restricted" or "hazard")
            .Select(a => (IReadOnlyList<IReadOnlyList<(double X, double Y)>>)a.Coordinates.Select(Local).ToList())
            .ToList();
        var graph = RouteGraphBuilder.Build(paths, blocked,
            new GraphSettings(nav.WalkingSpeedMps, nav.UnpavedSpeedFactor, nav.StepsSpeedFactor, nav.ConnectToleranceM));
        return (graph, projection);
    }

    private const int MaxLatestAttempts = 3;
}

public sealed class MapUploadConflictException() : Exception("Concurrent map uploads, try again");
