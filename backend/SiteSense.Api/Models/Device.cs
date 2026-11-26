using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

// PRV-01: a device is pseudonymous; it holds only its id, tenant and site. The id is unique per tenant (Q13).
public sealed class Device : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    public required string SiteId { get; init; }

    // Null when the id is already registered in this tenant, on any site (Q13): the store's version check runs under its per-document lock.
    public static async Task<Device?> CreateAsync(string tenantId, Device device, CancellationToken ct)
    {
        try
        {
            return await SiteSenseContext.Store.SaveAsync(tenantId, device, ct);
        }
        catch (ConcurrencyException)
        {
            return null;
        }
    }

    // Null for a missing device, another tenant's device, or an id the store would reject. No siteId needed: batch ingest looks devices up by tenant + deviceId.
    public static Task<Device?> GetAsync(string tenantId, string deviceId, CancellationToken ct) =>
        FileDocumentStore.IsValidName(deviceId) ? SiteSenseContext.Store.GetAsync<Device>(tenantId, deviceId, ct) : Task.FromResult<Device?>(null);
}
