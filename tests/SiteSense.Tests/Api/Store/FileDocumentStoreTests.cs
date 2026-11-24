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
        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);

        var saved = await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);

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
    public async Task Save_TwoTenants_UsesOneDirectoryPerTenant()
    {
        var store = new FileDocumentStore(_root);

        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);
        await store.SaveAsync("tenant-b", new TestDoc { Id = "x", TenantId = "tenant-b" }, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_root, "tenant-a", "TestDoc", "x.json")));
        Assert.True(File.Exists(Path.Combine(_root, "tenant-b", "TestDoc", "x.json")));
    }

    [Fact]
    public async Task Save_ParallelSameDocument_EndsAtVersionN()
    {
        var store = new FileDocumentStore(_root);

        var saves = Enumerable.Range(0, 100)
            .Select(_ => store.SaveAsync("tenant-a", new TestDoc { Id = "p", TenantId = "tenant-a" }, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(saves);
        var loaded = await store.GetAsync<TestDoc>("tenant-a", "p", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(100L, loaded.Version);
    }

    [Fact]
    public async Task Delete_Existing_ReturnsTrueThenGetNull()
    {
        var store = new FileDocumentStore(_root);
        await store.SaveAsync("tenant-a", new TestDoc { Id = "x", TenantId = "tenant-a" }, CancellationToken.None);

        var first = await store.DeleteAsync<TestDoc>("tenant-a", "x", CancellationToken.None);
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
