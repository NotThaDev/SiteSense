using SiteSense.Api.BackgroundJobs;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Jobs;

public sealed class ProbeDoc : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
}

// Test-only site job: one document per site and slot, created once (the JOB-02 pattern).
public sealed class ProbeSiteJob : ISiteJob
{
    public const string Name = "probe";

    public static string DocId(string siteId, long slotMs) => $"{Name}_{siteId}_{slotMs}";

    public async Task RunAsync(string tenantId, string siteId, long slotMs, CancellationToken ct)
    {
        try
        {
            await SiteSenseContext.Store.SaveAsync(tenantId, new ProbeDoc { Id = DocId(siteId, slotMs), TenantId = tenantId }, ct);
        }
        catch (ConcurrencyException)
        {
            // Already done for this slot.
        }
    }
}
