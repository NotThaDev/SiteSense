using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using SiteSense.Api.BackgroundJobs;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;
using SiteSense.Tests.Api.Auth;

namespace SiteSense.Tests.Api.Jobs;

// Api collection: the clock is a static shared by every test that reads it.
[Collection("Api")]
public sealed class SiteJobTests : IClassFixture<TestModeApiFactory>, IDisposable
{
    private const long NowMs = 1793268045123;
    private const long SlotMs = 1793268000000;
    private const string ProbeRun = "/api/v1/test/jobs/probe/run";

    // The real site from SS-18; each test swaps in its own siteId.
    private static readonly string ValidSiteJson = File.ReadAllText(Path.Combine(FindRepoRoot(), "data", "site.json"));

    private readonly TestModeApiFactory _factory;
    private readonly TimeProvider _originalTime = SiteSenseContext.Time;

    public SiteJobTests(TestModeApiFactory factory)
    {
        _factory = factory;
        // Build the host first: its startup sets the static clock.
        _ = _factory.Services;
        SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(NowMs));
    }

    public void Dispose() => SiteSenseContext.Time = _originalTime;

    [Fact]
    public void CurrentSlotMs_ClockMidMinute_RoundsDownToMinute()
    {
        // Arrange
        // The clock is set in the constructor to NowMs, which is 45.123 s into its minute.

        // Act
        var slot = SiteJobRunner.CurrentSlotMs();

        // Assert
        Assert.Equal(SlotMs, slot);
    }

    [Fact]
    public void Schedule_JobName_RegistersMinutelyRecurringJob()
    {
        // Arrange
        var manager = _factory.Services.GetRequiredService<IRecurringJobManager>();

        // Act
        SiteJobRunner.Schedule(manager, "probe-schedule");

        // Assert
        using var conn = _factory.Services.GetRequiredService<JobStorage>().GetConnection();
        var job = conn.GetRecurringJobs().Single(j => j.Id == "probe-schedule");
        Assert.Equal("* * * * *", job.Cron);
        manager.RemoveIfExists("probe-schedule");
    }

    [Fact]
    public async Task EnqueueSitesAsync_SitesInTwoTenants_EnqueuesOneRunPerSiteWithSameSlot()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteA1 = await CreateSiteAsync(admin);
        var siteA2 = await CreateSiteAsync(admin);
        var siteB = await CreateSiteAsync(otherAdmin);
        var expectedTenant = new Dictionary<string, string> { [siteA1] = "tenant-a", [siteA2] = "tenant-a", [siteB] = "tenant-b" };
        var client = new RecordingJobClient();
        var runner = new SiteJobRunner(_factory.Services, client);

        // Act
        await runner.EnqueueSitesAsync(ProbeSiteJob.Name, CancellationToken.None);

        // Assert
        var mine = client.Jobs.Where(j => expectedTenant.ContainsKey((string)j.Args[2])).ToList();
        Assert.Equal(3, mine.Count);
        foreach (var job in mine)
        {
            Assert.Equal("RunSiteAsync", job.Method.Name);
            Assert.Equal(ProbeSiteJob.Name, (string)job.Args[0]);
            Assert.Equal(expectedTenant[(string)job.Args[2]], (string)job.Args[1]);
            Assert.Equal(SlotMs, Convert.ToInt64(job.Args[3]));
        }
    }

    [Fact]
    public async Task RunSiteAsync_SameSlotTwice_CreatesOneDocument()
    {
        // Arrange
        var runner = _factory.Services.GetRequiredService<SiteJobRunner>();
        var siteId = NewSiteId();

        // Act
        await runner.RunSiteAsync(ProbeSiteJob.Name, "tenant-a", siteId, SlotMs, CancellationToken.None);
        await runner.RunSiteAsync(ProbeSiteJob.Name, "tenant-a", siteId, SlotMs, CancellationToken.None);

        // Assert
        var doc = await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs), CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public async Task RunSiteAsync_SameSlotConcurrent_CreatesOneDocument()
    {
        // Arrange
        var runner = _factory.Services.GetRequiredService<SiteJobRunner>();
        var siteId = NewSiteId();

        // Act
        await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => runner.RunSiteAsync(ProbeSiteJob.Name, "tenant-a", siteId, SlotMs, CancellationToken.None)));

        // Assert
        var doc = await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs), CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public async Task RunJob_TestModeAdmin_Returns204AfterRunFinished()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var response = await admin.PostAsync(ProbeRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var doc = await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs), CancellationToken.None);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task RunJob_TwiceSameClock_NoDuplicate()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var first = await admin.PostAsync(ProbeRun, null);
        var second = await admin.PostAsync(ProbeRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var doc = await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs), CancellationToken.None);
        Assert.NotNull(doc);
        Assert.Equal(1, doc.Version);
    }

    [Fact]
    public async Task RunJob_ClockMovedOneMinute_NewSlot()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var siteId = await CreateSiteAsync(admin);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));
        await admin.PostAsync(ProbeRun, null);

        // Act
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs + 60_000));
        var response = await admin.PostAsync(ProbeRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotNull(await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs), CancellationToken.None));
        Assert.NotNull(await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-a", ProbeSiteJob.DocId(siteId, SlotMs + 60_000), CancellationToken.None));
    }

    [Fact]
    public async Task RunJob_OnlyCallerTenantSites()
    {
        // Arrange
        var admin = AdminClient("tenant-a");
        var otherAdmin = AdminClient("tenant-b");
        var siteB = await CreateSiteAsync(otherAdmin);
        await admin.PutAsync("/api/v1/test/clock", ClockBody(NowMs));

        // Act
        var response = await admin.PostAsync(ProbeRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await SiteSenseContext.Store.GetAsync<ProbeDoc>("tenant-b", ProbeSiteJob.DocId(siteB, SlotMs), CancellationToken.None));
    }

    [Fact]
    public async Task RunJob_UnknownJob_Returns404ProblemDetails()
    {
        // Arrange
        var admin = AdminClient("tenant-a");

        // Act
        var response = await admin.PostAsync("/api/v1/test/jobs/no-such-job/run", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RunJob_NonAdmin_Returns403()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["Safety"]));

        // Act
        var response = await client.PostAsync(ProbeRun, null);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void Startup_RedisStorageWithoutConnectionString_Fails()
    {
        // Arrange
        using var factory = new ApiFactory().WithWebHostBuilder(b => b
            .UseSetting("Hangfire:Storage", "Redis")
            .UseSetting("ConnectionStrings:Redis", ""));

        // Act
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        // Assert
        Assert.Contains("ConnectionStrings:Redis", AllMessages(ex));
    }

    private static string AllMessages(Exception? ex) =>
        ex is null ? "" : ex.Message + " " + AllMessages(ex.InnerException);

    private HttpClient AdminClient(string tenant)
    {
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create(tenant, ["Admin"]));
        return client;
    }

    private static async Task<string> CreateSiteAsync(HttpClient admin)
    {
        var siteId = NewSiteId();
        var site = JsonNode.Parse(ValidSiteJson)!;
        site["siteId"] = siteId;
        var response = await admin.PostAsync("/api/v1/sites", new StringContent(site.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return siteId;
    }

    private static string NewSiteId() => $"site-{Guid.NewGuid():N}";

    private static HttpContent ClockBody(long nowMs) => JsonContent.Create(new { nowMs });

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        throw new InvalidOperationException("SiteSense.slnx not found above the test output directory.");
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public List<Job> Jobs { get; } = [];

        public string Create(Job job, IState state)
        {
            Jobs.Add(job);
            return Guid.NewGuid().ToString("N");
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
