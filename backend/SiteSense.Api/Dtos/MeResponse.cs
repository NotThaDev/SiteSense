namespace SiteSense.Api.Dtos;

public sealed record MeResponse(string TenantId, IReadOnlyList<string> Roles);
