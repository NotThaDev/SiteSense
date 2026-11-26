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

    // siteId null = every device of the tenant. Full scan, no index (SS-11, postponed); order is not fixed.
    public static async Task<IReadOnlyList<Device>> GetAllAsync(string tenantId, string? siteId = null, CancellationToken ct = default)
    {
        var all = await SiteSenseContext.Store.ListAsync<Device>(tenantId, ct);
        return siteId is null ? all : all.Where(d => d.SiteId == siteId).ToList();
    }

    // expectedVersion must be the stored version (from If-Match), else ConcurrencyException → 412 (Q45). False when missing.
    public static Task<bool> DeleteAsync(string tenantId, string deviceId, long expectedVersion, CancellationToken ct) =>
        SiteSenseContext.Store.DeleteAsync<Device>(tenantId, deviceId, expectedVersion, ct);
}
