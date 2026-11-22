using System.ComponentModel.DataAnnotations;

namespace SiteSense.Api.Models;

/// <summary>A WGS84 coordinate on the wire, always <c>{ "lat", "lon" }</c> in degrees. Map files use GeoJSON <c>[lon, lat]</c> instead.</summary>
public sealed record LatLon(
    [Range(-90.0, 90.0)] double Lat,
    [Range(-180.0, 180.0)] double Lon);
