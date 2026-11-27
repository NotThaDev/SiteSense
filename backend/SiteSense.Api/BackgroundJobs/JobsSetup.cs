using Hangfire;
using Hangfire.Redis.StackExchange;

namespace SiteSense.Api.BackgroundJobs;

public static class JobsSetup
{
    // Hangfire storage: Redis (Q08). InMemory only for the test host, since CI has no Redis (Q72).
    public static void AddSiteSenseJobs(this WebApplicationBuilder builder)
    {
        var storage = builder.Configuration["Hangfire:Storage"] ?? "Redis";
        var redis = builder.Configuration.GetConnectionString("Redis");
        if (storage is not ("Redis" or "InMemory"))
            throw new InvalidOperationException("Hangfire:Storage must be Redis or InMemory.");
        if (storage == "Redis" && string.IsNullOrWhiteSpace(redis))
            throw new InvalidOperationException("ConnectionStrings:Redis is required when Hangfire:Storage is Redis.");

        builder.Services.AddHangfire(c =>
        {
            c.SetDataCompatibilityLevel(CompatibilityLevel.Version_180).UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings();
            if (storage == "InMemory") c.UseInMemoryStorage();
            else c.UseRedisStorage(redis, new RedisStorageOptions { Prefix = "sitesense:" });
        });
        builder.Services.AddHangfireServer();
        builder.Services.AddTransient<SiteJobRunner>();
    }
}
