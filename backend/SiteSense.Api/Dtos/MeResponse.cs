namespace SiteSense.Api.Dtos;

// What the caller may do, from the authorization policies; the UI decides from these, never from role names (Q118).
public sealed record Capabilities(bool Manage, bool SeeIndividuals, bool SeeAggregates);

// Username is the preferred_username claim; null for gateway tokens without one.
public sealed record MeResponse(string TenantId, IReadOnlyList<string> Roles, string? Username, Capabilities Capabilities);
