namespace SiteSense.Api.BackgroundJobs;

// One periodic check for one site. Everything it writes gets an id built only from its inputs and is created with Version 0, so a retry of the same slot writes nothing new (JOB-02).
public interface ISiteJob
{
    Task RunAsync(string tenantId, string siteId, long slotMs, CancellationToken ct);
}
