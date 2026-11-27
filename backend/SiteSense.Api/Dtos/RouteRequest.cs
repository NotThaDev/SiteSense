using System.ComponentModel.DataAnnotations;

namespace SiteSense.Api.Dtos;

public sealed record LatLon(
    [Range(-90.0, 90.0, ErrorMessage = "lat must be between -90 and 90.")] double Lat,
    [Range(-180.0, 180.0, ErrorMessage = "lon must be between -180 and 180.")] double Lon);

public sealed record RouteRequest([Required] LatLon From, [Required] LatLon To);
