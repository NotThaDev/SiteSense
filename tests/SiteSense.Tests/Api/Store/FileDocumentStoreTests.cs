using SiteSense.Api.Store;

namespace SiteSense.Tests.Api.Store;

public sealed class FileDocumentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Save_New_SetsVersion1AndRoundTrips()
    {
        var store = new FileDocumentStore(_root);

        var saved = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Name = "first" }, CancellationToken.None);
        var loaded = await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None);

        Assert.Equal(1L, saved.Version);
        Assert.NotNull(loaded);
        Assert.Equal(1L, loaded.Version);
        Assert.Equal("first", loaded.Name);
    }

    [Fact]
    public async Task Save_Existing_IncrementsVersion()
    {
        var store = new FileDocumentStore(_root);
        var first = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);

        var saved = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Version = first.Version }, CancellationToken.None);

        Assert.Equal(2L, saved.Version);
    }

    [Fact]
    public async Task Save_TenantMismatch_Throws()
    {
        var store = new FileDocumentStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-b" }, CancellationToken.None));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("x\n")]
    public async Task Get_InvalidId_Throws(string id)
    {
        var store = new FileDocumentStore(_root);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetAsync<TestDoc>("tenant-a", id, CancellationToken.None));
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        var store = new FileDocumentStore(_root);

        var loaded = await store.GetAsync<TestDoc>("tenant-a", "nope", CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_IdsDifferingOnlyByCase_AreSeparateDocuments()
    {
        var store = new FileDocumentStore(_root);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "dev-001", TenantId = "tenant-a", Name = "lower" }, CancellationToken.None);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "DEV-001", TenantId = "tenant-a", Name = "upper" }, CancellationToken.None);

        var lower = await store.GetAsync<TestDoc>("tenant-a", "dev-001", CancellationToken.None);
        var upper = await store.GetAsync<TestDoc>("tenant-a", "DEV-001", CancellationToken.None);

        Assert.Equal(("lower", 1L, "dev-001"), (lower!.Name, lower.Version, lower.Id));
        Assert.Equal(("upper", 1L, "DEV-001"), (upper!.Name, upper.Version, upper.Id));
    }

    [Fact]
    public async Task Save_TwoTenants_UsesOneDirectoryPerTenant()
    {
        var store = new FileDocumentStore(_root);

        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);
        await store.SaveAsync("tenant-b", new TestDoc { Id = "x", TenantId = "tenant-b" }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "tenant-a", "TestDoc", "x.json")));
        Assert.True(File.Exists(Path.Combine(_root, "tenant-b", "TestDoc", "x.json")));
    }

    [Fact]
    public async Task Save_StaleVersion_ThrowsConcurrencyException()
    {
        var store = new FileDocumentStore(_root);
        var v1 = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Name = "first" }, CancellationToken.None);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Version = v1.Version, Name = "second" }, CancellationToken.None);

        var stale = new TestDoc { Id = "x", TenantId = "tenant-a", Version = 1, Name = "stale" };
        await Assert.ThrowsAsync<ConcurrencyException>(() => store.SaveAsync("tenant-a", stale, CancellationToken.None));
        var loaded = await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None);

        Assert.Equal(1L, stale.Version);
        Assert.Equal(("second", 2L), (loaded!.Name, loaded.Version));
    }

    [Fact]
    public async Task Delete_StaleVersion_ThrowsConcurrencyException()
    {
        var store = new FileDocumentStore(_root);
        var v1 = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Version = v1.Version }, CancellationToken.None);

        await Assert.ThrowsAsync<ConcurrencyException>(() => store.DeleteAsync<TestDoc>("tenant-a", "x", 1, CancellationToken.None));

        Assert.NotNull(await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None));
    }

    [Fact]
    public async Task Save_ConcurrentWritersSameVersion_ExactlyOneWins()
    {
        const int writers = 16;
        var store = new FileDocumentStore(_root);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Name = "start" }, CancellationToken.None);
        using var barrier = new Barrier(writers);

        var results = await Task.WhenAll(Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
        {
            var read = await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None);
            barrier.SignalAndWait();
            try
            {
                await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a", Version = read!.Version, Name = $"w{i}" }, CancellationToken.None);
                return $"w{i}";
            }
            catch (ConcurrencyException)
            {
                return null;
            }
        })));
        var loaded = await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None);

        var winners = results.Where(r => r is not null).ToArray();
        Assert.Single(winners);
        Assert.Equal((winners[0], 2L), (loaded!.Name, loaded.Version));
    }

    [Fact]
    public async Task Delete_Existing_ReturnsTrueThenGetNull()
    {
        var store = new FileDocumentStore(_root);
        var saved = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);

        var first = await store.DeleteAsync<TestDoc>("tenant-a", "x", saved.Version, CancellationToken.None);
        var loaded = await store.GetAsync<TestDoc>("tenant-a", "x", CancellationToken.None);

        Assert.True(first);
        Assert.Null(loaded);
    }

    [Fact]
    public void Ctor_OrphanTmp_IsDeleted()
    {
        var dir = Path.Combine(_root, "tenant-a", "TestDoc");
        Directory.CreateDirectory(dir);
        var orphan = Path.Combine(dir, "x.json.tmp");
        File.WriteAllText(orphan, "{ partial");

        _ = new FileDocumentStore(_root);

        Assert.False(File.Exists(orphan));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
