using SiteSense.Api.Helpers;
using SiteSense.Api.Models;

namespace SiteSense.Api.BackgroundJobs;

// Deletes raw position and track hour files older than the tenant's RetentionDays, on a rolling UTC window of the simulated clock (PRV-04, JOB-04, Q48).
// Only deletes whole expired files, so a retry or a run after a crash is safe (JOB-02).
public sealed class RetentionJob(ILogger<RetentionJob> logger) : ITenantJob
{
    public const string Name = "retention";
    private const long DayMs = 86_400_000;

    public async Task RunAsync(string tenantId, long slotMs, CancellationToken ct)
    {
        if (!SiteSenseContext.Tenants.TryGetValue(tenantId, out var tenant))
        {
            logger.LogWarning("Retention skipped for {TenantId}: tenant not in configuration", tenantId);
            return;
        }
        var cutoffMs = slotMs - tenant.RetentionDays * DayMs;
        // The processed track (TrackPoint, Q16, Q101) is personal location data too: same cutoff.
        var deleted = await SiteSenseContext.Log.DeleteBeforeAsync<Position>(tenantId, cutoffMs, ct)
            + await SiteSenseContext.Log.DeleteBeforeAsync<TrackPoint>(tenantId, cutoffMs, ct);
        logger.LogInformation("Retention for {TenantId}: {Deleted} hour files deleted before {CutoffMs}", tenantId, deleted, cutoffMs);
    }
}
