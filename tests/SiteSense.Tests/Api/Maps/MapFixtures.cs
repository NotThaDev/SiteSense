namespace SiteSense.Tests.Api.Maps;

internal static class MapFixtures
{
    // Valid map: perimeter, work area, paved path, assembly point and entrance on the path.
    // Mutation tests edit this text; keep the exact spacing of "areaType": "perimeter".
    public const string BaseMap = """
    {
      "type": "FeatureCollection",
      "features": [
        { "type": "Feature",
          "geometry": { "type": "Polygon", "coordinates": [[[15.06, 37.51], [15.08, 37.51], [15.08, 37.53], [15.06, 37.53], [15.06, 37.51]]] },
          "properties": { "id": "perimeter", "kind": "area", "name": "Site", "areaType": "perimeter" } },
        { "type": "Feature",
          "geometry": { "type": "Polygon", "coordinates": [[[15.0700, 37.5240], [15.0720, 37.5240], [15.0720, 37.5245], [15.0700, 37.5245], [15.0700, 37.5240]]] },
          "properties": { "id": "work-1", "kind": "area", "name": "Yard", "areaType": "work", "capacity": 30 } },
        { "type": "Feature",
          "geometry": { "type": "LineString", "coordinates": [[15.0705, 37.5250], [15.0715, 37.5250]] },
          "properties": { "id": "p1", "kind": "path", "surface": "paved", "steps": false, "slopePercent": 0, "widthM": 2 } },
        { "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [15.0710, 37.5250] },
          "properties": { "id": "ap-1", "kind": "assemblyPoint", "name": "Assembly A", "capacity": 50 } },
        { "type": "Feature",
          "geometry": { "type": "Point", "coordinates": [15.0705, 37.5250] },
          "properties": { "id": "e-1", "kind": "entrance", "name": "Gate A" } }
      ]
    }
    """;

    // The real site map from data/ (SS-18), with its site's connectToleranceM.
    public static readonly string RealMap = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "map.geojson"));
    public const double RealMapToleranceM = 0.5;

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }
}
