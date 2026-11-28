using SiteSense.Api.Services;

namespace SiteSense.Api.Dtos;

public sealed record OccupancyResponse(long AtMs, IReadOnlyList<AreaCount> Areas);
