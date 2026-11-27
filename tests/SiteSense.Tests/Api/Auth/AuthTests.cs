using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SiteSense.Tests.Api.Auth;

[Collection("Api")]
public sealed class AuthTests : IClassFixture<ApiFactory>
{
    private const string ProblemJson = "application/problem+json";

    private readonly ApiFactory _factory;

    public AuthTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("tenant-a", "Admin")]
    [InlineData("tenant-a", "Safety")]
    [InlineData("tenant-a", "Viewer")]
    [InlineData("tenant-b", "Admin")]
    [InlineData("tenant-b", "Safety")]
    [InlineData("tenant-b", "Viewer")]
    public async Task Me_ValidToken_ReturnsTenantAndRoles(string tenant, string role)
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create(tenant, [role]));

        // Act
        var response = await client.GetAsync("/api/v1/me");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(tenant, doc.RootElement.GetProperty("tenantId").GetString());
        Assert.Equal(new[] { role }, doc.RootElement.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task Me_ReturnsUsernameFromPreferredUsername()
    {
        // Arrange
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"], username: "admin-a"));

        // Act
        var response = await client.GetAsync("/api/v1/me");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("admin-a", doc.RootElement.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Me_TokenWithoutRoles_ReturnsEmptyRoles()
    {
        // Arrange (gateway-like token: no roles claim)
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a"));

        // Act
        var response = await client.GetAsync("/api/v1/me");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(doc.RootElement.GetProperty("roles").EnumerateArray());
    }

    [Fact]
    public async Task Me_UnknownTenant_Returns403Problem()
    {
        // Arrange (valid signature, tenant not in configuration)
        var client = _factory.CreateClient().WithToken(TestTokens.Create("tenant-x", ["Admin"]));

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_NoToken_Returns401Problem()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(ProblemJson, response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-key")]
    [InlineData("expired")]
    [InlineData("no-tenant")]
    [InlineData("bad-tenant")]
    [InlineData("two-tenants")]
    public async Task Me_InvalidToken_Returns401(string testCase)
    {
        // Arrange
        var token = testCase switch
        {
            "wrong-issuer" => TestTokens.Create(issuer: "http://evil:8081/realms/sitesense"),
            "wrong-audience" => TestTokens.Create(audience: "other-api"),
            "wrong-key" => TestTokens.Create(key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }),
            "expired" => TestTokens.Create(expires: DateTime.UtcNow.AddMinutes(-2)),
            "no-tenant" => TestTokens.Create(noTenant: true),
            "bad-tenant" => TestTokens.Create("../x"),
            "two-tenants" => TestTokens.Create(new[] { "tenant-a", "tenant-b" }),
            _ => throw new ArgumentOutOfRangeException(nameof(testCase)),
        };
        var client = _factory.CreateClient().WithToken(token);

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_NoToken_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/nope");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Authority_WithoutMetadataAddress_IsTheIssuer()
    {
        // Arrange
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        // Act
        var authority = options.Get(JwtBearerDefaults.AuthenticationScheme).Authority;

        // Assert
        Assert.Equal(TestTokens.Issuer, authority);
    }

    [Fact]
    public void Authority_WithMetadataAddress_IsNotSet()
    {
        // Arrange
        const string metadata = "http://keycloak:8081/realms/sitesense/.well-known/openid-configuration";
        using var factory = _factory.WithWebHostBuilder(b => b.UseSetting("Auth:MetadataAddress", metadata));
        var options = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>();

        // Act
        var result = options.Get(JwtBearerDefaults.AuthenticationScheme);

        // Assert
        Assert.Null(result.Authority);
        Assert.Equal(metadata, result.MetadataAddress);
    }
}
