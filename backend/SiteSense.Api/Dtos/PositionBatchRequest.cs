using System.ComponentModel.DataAnnotations;

namespace SiteSense.Api.Dtos;

// The gateway batch: one device, up to 5,000 positions, metres and Unix epoch ms UTC. One bad position rejects the whole batch (Q44).
public sealed record PositionBatchRequest([Required] string DeviceId, [Required, MinLength(1), MaxLength(5000)] IReadOnlyList<PositionRequest> Positions);

// Messages name the field only, never the rejected value (no coordinates in responses, Q62).
public sealed record PositionRequest(
    [Range(0L, 253402300799999L, ErrorMessage = "timestampMs must be between 0 and 253402300799999.")] long TimestampMs,
    [Range(-90.0, 90.0, ErrorMessage = "lat must be between -90 and 90.")] double Lat,
    [Range(-180.0, 180.0, ErrorMessage = "lon must be between -180 and 180.")] double Lon,
    [Range(0.0, double.MaxValue, ErrorMessage = "accuracyM must be 0 or more.")] double AccuracyM);
