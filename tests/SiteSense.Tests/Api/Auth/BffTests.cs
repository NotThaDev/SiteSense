using System.Net;
using System.Security.Claims;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SiteSense.Tests.Api.Auth;

// The browser login (BFF): cookie users and the login redirect, without Keycloak.
[Collection("Api")]
public sealed class BffTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public BffTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_RedirectsToAuthorizeEndpointWithPkce()
    {
        // Arrange
        var client = NoRedirectClient();

        // Act
        var response = await client.GetAsync("/api/v1/auth/login?returnUrl=/sites");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith(TestTokens.Issuer + "/protocol/openid-connect/auth", location.ToString());
        var query = HttpUtility.ParseQueryString(location.Query);
        Assert.Equal("sitesense-ui", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.False(string.IsNullOrEmpty(query["code_challenge"]));
        Assert.Equal("/sites", ReturnUrlOf(query["state"]!));
    }

    [Theory]
    [InlineData("https://evil.example/x")]
    [InlineData("//evil.example/x")]
    public async Task Login_ExternalReturnUrl_FallsBackToRoot(string returnUrl)
    {
        // Arrange
        var client = NoRedirectClient();

        // Act
        var response = await client.GetAsync($"/api/v1/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = HttpUtility.ParseQueryString(response.Headers.Location!.Query);
        Assert.Equal("/", ReturnUrlOf(query["state"]!));
    }

    [Fact]
    public async Task Cookie_WithCsrfHeader_IsAuthenticated()
    {
        // Arrange
        var client = CookieClient("tenant-a", "Admin");
        client.DefaultRequestHeaders.Add("X-CSRF", "1");

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("tenant-a", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cookie_WithoutCsrfHeader_Returns403()
    {
        // Arrange
        var client = CookieClient("tenant-a", "Admin");

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Cookie_WithNonBearerAuthorizationAndNoCsrfHeader_Returns403()
    {
        // Arrange
        var client = CookieClient("tenant-a", "Admin");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Basic eDp4");

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoCredentials_Returns401NotRedirect()
    {
        // Arrange
        var client = NoRedirectClient();

        // Act
        var response = await client.GetAsync("/api/v1/me");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Cookie_OtherTenant_Returns404()
    {
        // Arrange
        var siteId = $"site-{Guid.NewGuid():N}";
        var owner = _factory.CreateClient().WithToken(TestTokens.Create("tenant-a", ["Admin"]));
        var created = await owner.PostAsync("/api/v1/sites", SiteJson(siteId));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var other = CookieClient("tenant-b", "Admin");
        other.DefaultRequestHeaders.Add("X-CSRF", "1");

        // Act
        var response = await other.GetAsync($"/api/v1/sites/{siteId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Same cookie the API would issue after a Keycloak login, made with the cookie scheme's own protector.
    private HttpClient CookieClient(string tenant, string role)
    {
        var options = _factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var identity = new ClaimsIdentity(
            [new Claim("sub", "user-1"), new Claim("tenant_id", tenant), new Claim("roles", role)],
            "test", "preferred_username", "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), CookieAuthenticationDefaults.AuthenticationScheme);
        var client = NoRedirectClient();
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={options.TicketDataFormat.Protect(ticket)}");
        return client;
    }

    private string ReturnUrlOf(string state) =>
        _factory.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme).StateDataFormat.Unprotect(state)!.RedirectUri!;

    private static StringContent SiteJson(string siteId)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "site.json");
        var site = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        site["siteId"] = siteId;
        return new StringContent(site.ToJsonString(), System.Text.Encoding.UTF8, "application/json");
    }
}
