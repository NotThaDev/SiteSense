using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Services;

// Areas in metres for SS-25 (Q27, Q28, Q119). Projected from the perimeter centroid, as PositionIngest does.
public sealed class SiteAreas
{
    private const double BoundaryToleranceM = 1e-6;

    private readonly string _perimeterId;
    private readonly LocalProjection _projection;
    private readonly Dictionary<string, IReadOnlyList<IReadOnlyList<(double X, double Y)>>> _rings = [];
    // Non-perimeter areas in map order: the first one containing a point wins (Q119).
    private readonly List<string> _subAreas = [];

    private SiteAreas(MapArea perimeter, IEnumerable<MapArea> areas)
    {
        _perimeterId = perimeter.Id;
        _projection = LocalProjection.FromCentroid(perimeter.Coordinates[0].Select(c => (Lon: c[0], Lat: c[1])).ToList());
        foreach (var a in areas)
        {
            _rings[a.Id] = Project(a.Coordinates);
            if (a.AreaType != "perimeter") _subAreas.Add(a.Id);
        }
    }

    // Null when the map has no perimeter: nothing can be assigned.
    public static SiteAreas? From(SiteMap map)
    {
        var perimeter = map.Areas.FirstOrDefault(a => a.AreaType == "perimeter");
        return perimeter is null ? null : new SiteAreas(perimeter, map.Areas);
    }

    // The first sub-area containing the point (tolerance 1e-6 m), else the perimeter (Q28).
    public string Candidate(double lon, double lat)
    {
        foreach (var id in _subAreas)
            if (Locate(id, lon, lat, BoundaryToleranceM) != PointLocation.Outside) return id;
        return _perimeterId;
    }

    public bool Knows(string areaId) => _rings.ContainsKey(areaId);

    public bool IsPerimeter(string areaId) => areaId == _perimeterId;

    // Callers check Knows first: an unknown id throws.
    public PointLocation Locate(string areaId, double lon, double lat, double tolerance) =>
        PlanarGeometry.Locate(_projection.ToLocal(lon, lat), _rings[areaId], tolerance);

    private IReadOnlyList<IReadOnlyList<(double X, double Y)>> Project(double[][][] rings) =>
        rings.Select(r => (IReadOnlyList<(double X, double Y)>)r.Select(c => _projection.ToLocal(c[0], c[1])).ToList()).ToList();
}
