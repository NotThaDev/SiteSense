namespace SiteSense.Api.BackgroundJobs;

// One periodic task for one whole tenant, e.g. retention (Q96). A retry of the same slot must change nothing more (JOB-02).
public interface ITenantJob
{
    Task RunAsync(string tenantId, long slotMs, CancellationToken ct);
}
