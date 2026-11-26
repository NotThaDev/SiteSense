namespace SiteSense.Api.Dtos;

// Username is the preferred_username claim; null for gateway tokens without one.
public sealed record MeResponse(string TenantId, IReadOnlyList<string> Roles, string? Username);
