namespace SiteSense.Api.Models;

public sealed record MeResponse(string TenantId, IReadOnlyList<string> Roles);
