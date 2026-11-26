namespace SiteSense.Domain.Geometry;

/// <summary>
/// Converts GPS coordinates (degrees of longitude and latitude) into flat x/y meters for one site map (MAP-02),
/// so distances and tolerances can be computed in meters. It does not hold a position: it is the converter.
/// The origin (0, 0) is a reference point of the site, usually its centre; x grows east, y grows north.
/// Example: with the origin at the site centre, a point 10 m north of it becomes (0, 10).
/// Method: equirectangular on the WGS84 ellipsoid, using the meridional and prime-vertical radii at the origin.
/// shortcut: distortion grows with distance from the origin (~mm at 1 km); switch to ProjNet/UTM if sites exceed ~10 km.
/// </summary>
public sealed class LocalProjection
{
    private const double A = 6378137.0;               // WGS84 semi-major axis (m)
    private const double E2 = 6.69437999014e-3;       // WGS84 first eccentricity squared
    private const double Deg = Math.PI / 180.0;

    private readonly double _metersPerDegLon;
    private readonly double _metersPerDegLat;

    public double OriginLon { get; }
    public double OriginLat { get; }

    public LocalProjection(double originLon, double originLat)
    {
        if (originLat is < -89 or > 89 || originLon is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(originLat), "Origin must be a valid WGS84 point away from the poles.");
        OriginLon = originLon;
        OriginLat = originLat;
        var s = Math.Sin(originLat * Deg);
        var w = Math.Sqrt(1 - E2 * s * s);
        _metersPerDegLon = A / w * Math.Cos(originLat * Deg) * Deg;  // prime vertical radius
        _metersPerDegLat = A * (1 - E2) / (w * w * w) * Deg;          // meridional radius
    }

    /// <summary>Projection centred on the mean of the given (lon, lat) points, e.g. a perimeter.</summary>
    public static LocalProjection FromCentroid(IReadOnlyCollection<(double Lon, double Lat)> points)
    {
        if (points.Count == 0) throw new ArgumentException("At least one point is required.", nameof(points));
        return new LocalProjection(points.Average(p => p.Lon), points.Average(p => p.Lat));
    }

    public (double X, double Y) ToLocal(double lon, double lat) =>
        ((lon - OriginLon) * _metersPerDegLon, (lat - OriginLat) * _metersPerDegLat);

    public (double Lon, double Lat) ToWgs84(double x, double y) =>
        (OriginLon + x / _metersPerDegLon, OriginLat + y / _metersPerDegLat);
}
