namespace SiteSense.Api.Dtos;

// GeoJSON LineString, coordinates [lon, lat].
public sealed record LineStringGeometry(string Type, double[][] Coordinates);

public sealed record RouteResponse(LineStringGeometry Geometry, double DistanceM, double DurationS);
