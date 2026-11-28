using System.ComponentModel.DataAnnotations;

namespace SiteSense.Api.Dtos;

// Track query (Q37, Q120, Q121): range [fromMs, toMs) in Unix ms UTC. Both times are required so a call never scans every hour file.
// Messages name the field only, never the rejected value.
public sealed record TrackRequest(
    [Required(ErrorMessage = "fromMs is required."), Range(0L, long.MaxValue, ErrorMessage = "fromMs must be 0 or more.")] long? FromMs,
    [Required(ErrorMessage = "toMs is required."), Range(0L, long.MaxValue, ErrorMessage = "toMs must be 0 or more.")] long? ToMs,
    [Range(1, 5000, ErrorMessage = "limit must be between 1 and 5000.")] int Limit = 1000,
    string? Cursor = null);
