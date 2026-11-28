using System.Text.Json.Serialization;
using SiteSense.Api.Models;

namespace SiteSense.Api.Dtos;

// NextCursor is omitted on the last page. Clients treat it as opaque (Q37).
public sealed record TrackResponse(IReadOnlyList<TrackPointResponse> Points, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null);

public sealed record TrackPointResponse(long TimestampMs, double Lat, double Lon, string? AreaId)
{
    public static TrackPointResponse From(TrackPoint p) => new(p.TimestampMs, p.Lat, p.Lon, p.AreaId);
}
