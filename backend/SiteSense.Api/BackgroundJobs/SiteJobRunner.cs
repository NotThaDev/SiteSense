using Hangfire;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;

namespace SiteSense.Api.BackgroundJobs;

// Fans a site job out to every site, one minute slot per run (JOB-01). The slot is a Hangfire job argument, so a retry reuses it (JOB-02).
public sealed class SiteJobRunner(IServiceProvider services, IBackgroundJobClient jobs)
{
    // Minute of the current (possibly simulated, Q38) time, in Unix ms.
    public static long CurrentSlotMs()
    {
        var ms = SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds();
        return ms - ms % 60_000;
    }

    public static void Schedule(IRecurringJobManager manager, string jobName) =>
        manager.AddOrUpdate<SiteJobRunner>(jobName, r => r.EnqueueSitesAsync(jobName, CancellationToken.None), Cron.Minutely());

    public async Task EnqueueSitesAsync(string jobName, CancellationToken ct)
    {
        var slotMs = CurrentSlotMs();
        foreach (var tenantId in SiteSenseContext.Tenants.Keys)
            foreach (var site in await Site.ListAsync(tenantId, ct))
            {
                var siteId = site.Id;
                jobs.Enqueue<SiteJobRunner>(r => r.RunSiteAsync(jobName, tenantId, siteId, slotMs, CancellationToken.None));
            }
    }

    public Task RunSiteAsync(string jobName, string tenantId, string siteId, long slotMs, CancellationToken ct) =>
        services.GetRequiredKeyedService<ISiteJob>(jobName).RunAsync(tenantId, siteId, slotMs, ct);

    // Tenant jobs (Q96): one Hangfire job per configured tenant on each run.
    public static void ScheduleTenantJob(IRecurringJobManager manager, string jobName, string cron) =>
        manager.AddOrUpdate<SiteJobRunner>(jobName, r => r.EnqueueTenantsAsync(jobName, CancellationToken.None), cron);

    public Task EnqueueTenantsAsync(string jobName, CancellationToken ct)
    {
        var slotMs = CurrentSlotMs();
        foreach (var tenantId in SiteSenseContext.Tenants.Keys)
            jobs.Enqueue<SiteJobRunner>(r => r.RunTenantAsync(jobName, tenantId, slotMs, CancellationToken.None));
        return Task.CompletedTask;
    }

    public Task RunTenantAsync(string jobName, string tenantId, long slotMs, CancellationToken ct) =>
        services.GetRequiredKeyedService<ITenantJob>(jobName).RunAsync(tenantId, slotMs, ct);

    // False for an unknown job name. Only the caller's tenant runs (tenant isolation); returns when the tenant job or every site is done (C-7.2c).
    public async Task<bool> RunNowAsync(string jobName, string tenantId, CancellationToken ct)
    {
        var slotMs = CurrentSlotMs();
        if (services.GetKeyedService<ITenantJob>(jobName) is not null)
        {
            await RunTenantAsync(jobName, tenantId, slotMs, ct);
            return true;
        }
        if (services.GetKeyedService<ISiteJob>(jobName) is null) return false;
        foreach (var site in await Site.ListAsync(tenantId, ct))
            await RunSiteAsync(jobName, tenantId, site.Id, slotMs, ct);
        return true;
    }
}
