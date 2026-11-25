using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api.Auth;

[Collection("Api")]
public sealed class PolicyTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public PolicyTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("admin", "individuals")]
    [InlineData("admin", "positions")]
    [InlineData("safety", "manage")]
    [InlineData("safety", "positions")]
    [InlineData("viewer", "manage")]
    [InlineData("viewer", "individuals")]
    [InlineData("viewer", "positions")]
    [InlineData("gateway", "manage")]
    [InlineData("gateway", "individuals")]
    [InlineData("gateway", "aggregates")]
    [InlineData("norole", "manage")]
    [InlineData("norole", "individuals")]
    [InlineData("norole", "aggregates")]
    [InlineData("norole", "positions")]
    public async Task Policy_DeniedCaller_Returns403Problem(string caller, string route)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.GetAsync($"/probe/policy/{route}/missing-id");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
    }

    [Theory]
    [InlineData("admin", "manage")]
    [InlineData("admin", "aggregates")]
    [InlineData("safety", "individuals")]
    [InlineData("safety", "aggregates")]
    [InlineData("viewer", "aggregates")]
    [InlineData("gateway", "positions")]
    [InlineData("admin+scope", "manage")]
    [InlineData("admin+scope", "positions")]
    public async Task Policy_GrantedCaller_OwnTenantDocument_Returns200(string caller, string route)
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = $"own-{caller.Replace('+', '-')}-{route}"; // '+' is not a valid store name
        await SiteSenseContext.Store.SaveAsync("tenant-a", new TestDoc { Id = id, TenantId = "tenant-a", Name = "a" }, CancellationToken.None);
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.GetAsync($"/probe/policy/{route}/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("admin", "manage")]
    [InlineData("admin", "aggregates")]
    [InlineData("safety", "individuals")]
    [InlineData("safety", "aggregates")]
    [InlineData("viewer", "aggregates")]
    [InlineData("gateway", "positions")]
    [InlineData("admin+scope", "manage")]
    [InlineData("admin+scope", "positions")]
    public async Task Policy_GrantedCaller_OtherTenantDocument_Returns404(string caller, string route)
    {
        // Arrange
        var client = _factory.CreateClient();
        var id = $"other-{caller.Replace('+', '-')}-{route}"; // '+' is not a valid store name
        await SiteSenseContext.Store.SaveAsync("tenant-b", new TestDoc { Id = id, TenantId = "tenant-b", Name = "b" }, CancellationToken.None);
        client.WithToken(TokenFor(caller));

        // Act
        var response = await client.GetAsync($"/probe/policy/{route}/{id}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("positions:write2")]
    [InlineData("xpositions:write")]
    [InlineData("positions:read")]
    [InlineData("POSITIONS:WRITE")]
    public async Task ScopePolicy_LookAlikeScope_Returns403(string scope)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", scope: scope));

        // Act
        var response = await client.GetAsync("/probe/policy/positions/missing-id");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ScopePolicy_ArrayScope_Returns200()
    {
        // Arrange
        var client = _factory.CreateClient();
        await SiteSenseContext.Store.SaveAsync("tenant-a", new TestDoc { Id = "arr-doc", TenantId = "tenant-a", Name = "a" }, CancellationToken.None);
        client.WithToken(TestTokens.Create("tenant-a", scope: new[] { "profile", "positions:write" }));

        // Act
        var response = await client.GetAsync("/probe/policy/positions/arr-doc");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RolePolicy_LowerCaseRole_Returns403()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.WithToken(TestTokens.Create("tenant-a", ["admin"]));

        // Act
        var response = await client.GetAsync("/probe/policy/manage/missing-id");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("manage")]
    [InlineData("individuals")]
    [InlineData("aggregates")]
    [InlineData("positions")]
    public async Task Policy_NoToken_Returns401(string route)
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/probe/policy/{route}/missing-id");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Backend_DoesNotCheckRolesInCode()
    {
        // Arrange
        var root = FindRepoRoot();
        var backend = Path.Combine(root, "backend");
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(backend, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal))
            .ToList();
        var pattern = new Regex(@"IsInRole|\bRoles\s*=");

        // Act
        var matches = files
            .Where(f => pattern.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();

        // Assert
        Assert.True(files.Count > 0, "No backend .cs files found.");
        Assert.Empty(matches);
    }

    private static string TokenFor(string caller, string tenant = "tenant-a") => caller switch
    {
        "admin" => TestTokens.Create(tenant, ["Admin"]),
        "safety" => TestTokens.Create(tenant, ["Safety"]),
        "viewer" => TestTokens.Create(tenant, ["Viewer"]),
        "gateway" => TestTokens.Create(tenant, scope: "positions:write"),
        "norole" => TestTokens.Create(tenant),
        "admin+scope" => TestTokens.Create(tenant, ["Admin"], scope: "profile positions:write"),
        _ => throw new ArgumentOutOfRangeException(nameof(caller), caller, "Unknown test caller."),
    };

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "SiteSense.slnx"))) return dir.FullName;
        Assert.Fail("SiteSense.slnx not found above the test output directory.");
        return string.Empty;
    }
}
