using SiteSense.Api.Services;

namespace SiteSense.Api.BackgroundJobs;

// Per minute: a device that went silent drops out of the count, so the push agrees with GET /occupancy (SS-27, Q122). Writes nothing itself, so a retry is safe (JOB-02).
public sealed class OccupancySiteJob(OccupancyService occupancy) : ISiteJob
{
    public const string Name = "occupancy";

    public Task RunAsync(string tenantId, string siteId, long slotMs, CancellationToken ct) =>
        occupancy.PublishIfChangedAsync(tenantId, siteId, ct);
}
