using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

public sealed record NavigationSettings(double WalkingSpeedMps, double UnpavedSpeedFactor, double StepsSpeedFactor, double ConnectToleranceM, double MaxSnapDistanceM);

public sealed class Site : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    public required string Name { get; init; }
    public required string TimeZone { get; init; }
    public required string MapFile { get; init; }
    public required NavigationSettings Navigation { get; init; }

    // Creates (Version 0) or overwrites (Version = stored) a site; any other version throws ConcurrencyException (Q43, Q45).
    public static Task<Site> SaveAsync(string tenantId, Site site, CancellationToken ct) =>
        SiteSenseContext.Store.SaveAsync(tenantId, site, ct);

    // Null for a missing site, another tenant's site, or an id the store would reject.
    public static Task<Site?> GetAsync(string tenantId, string id, CancellationToken ct) =>
        FileDocumentStore.IsValidName(id) ? SiteSenseContext.Store.GetAsync<Site>(tenantId, id, ct) : Task.FromResult<Site?>(null);
}
