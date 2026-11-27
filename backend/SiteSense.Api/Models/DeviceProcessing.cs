using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

// Per-device processing state and durable work marker (TRK-02, Q100, Q103). Id = deviceId.
public sealed class DeviceProcessing : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    // Latest raw timestamp already processed; null before the first run.
    public long? CursorMs { get; set; }
    // Track point revision; a late position raises it and replays (Q06).
    public long Revision { get; set; }
    // Oldest raw timestamp not processed yet; null when nothing is pending.
    public long? DirtyFromMs { get; set; }

    public static Task<DeviceProcessing?> GetAsync(string tenantId, string deviceId, CancellationToken ct) =>
        SiteSenseContext.Store.GetAsync<DeviceProcessing>(tenantId, deviceId, ct);

    public static Task<IReadOnlyList<DeviceProcessing>> ListAsync(string tenantId, CancellationToken ct) =>
        SiteSenseContext.Store.ListAsync<DeviceProcessing>(tenantId, ct);

    // Version check: throws ConcurrencyException when doc.Version is not the stored one.
    public static Task<DeviceProcessing> SaveAsync(string tenantId, DeviceProcessing doc, CancellationToken ct) =>
        SiteSenseContext.Store.SaveAsync(tenantId, doc, ct);

    // DirtyFromMs = min(current, fromMs). Always saves, so the version goes up even when the minimum is unchanged:
    // a processor run that read the raw log before this mark then fails its version check and runs again.
    public static async Task MarkDirtyAsync(string tenantId, string deviceId, long fromMs, CancellationToken ct)
    {
        while (true)
        {
            var doc = await GetAsync(tenantId, deviceId, ct) ?? new DeviceProcessing { Id = deviceId, TenantId = tenantId };
            doc.DirtyFromMs = doc.DirtyFromMs is { } d ? Math.Min(d, fromMs) : fromMs;
            try
            {
                await SaveAsync(tenantId, doc, ct);
                return;
            }
            catch (ConcurrencyException)
            {
                // Another mark or the processor saved first: reload and apply again.
            }
        }
    }
}
