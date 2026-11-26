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
    public required NavigationSettings Navigation { get; init; }

    // Null when the site already exists in this tenant (Q43): the store's version check runs under its per-document lock.
    public static async Task<Site?> CreateAsync(string tenantId, Site site, CancellationToken ct)
    {
        try
        {
            return await SiteSenseContext.Store.SaveAsync(tenantId, site, ct);
        }
        catch (ConcurrencyException)
        {
            return null;
        }
    }

    // site.Version must be the stored version (from If-Match), else ConcurrencyException → 412 (Q45).
    public static Task<Site> UpdateAsync(string tenantId, Site site, CancellationToken ct) =>
        SiteSenseContext.Store.SaveAsync(tenantId, site, ct);

    // Null for a missing site, another tenant's site, or an id the store would reject.
    public static Task<Site?> GetAsync(string tenantId, string id, CancellationToken ct) =>
        FileDocumentStore.IsValidName(id) ? SiteSenseContext.Store.GetAsync<Site>(tenantId, id, ct) : Task.FromResult<Site?>(null);
}
