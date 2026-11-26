using System.ComponentModel.DataAnnotations;

namespace SiteSense.Api.Dtos;

// Bounds of DateTimeOffset.FromUnixTimeMilliseconds.
public sealed record TestClockRequest([Range(-62135596800000L, 253402300799999L)] long NowMs);
