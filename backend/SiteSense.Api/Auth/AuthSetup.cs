using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SiteSense.Api.Realtime;
using SiteSense.Api.Store;

namespace SiteSense.Api.Auth;

public static class AuthSetup
{
    public const string SmartScheme = "Smart";
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    // JWT callers: a Bearer header, or the hub's access_token query (SignalR WebSockets cannot send headers). Everything else is the UI cookie.
    public static bool UsesJwt(HttpRequest request) =>
        request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || (request.Path.StartsWithSegments(SiteHub.RealtimePath, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(request.Headers.Authorization) && request.Query.ContainsKey("access_token"));

    public static void AddSiteSenseAuth(this WebApplicationBuilder builder, bool testMode)
    {
        var options = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer)) failures.Add("Auth:Issuer is required.");
        if (string.IsNullOrWhiteSpace(options.Audience)) failures.Add("Auth:Audience is required.");
        if (failures.Count > 0)
            throw new OptionsValidationException(AuthOptions.Section, typeof(AuthOptions), failures);

        builder.Services.AddAuthentication(SmartScheme)
            // Bearer header: JWT (gateways, scripts, tests); otherwise the UI cookie.
            .AddPolicyScheme(SmartScheme, null, o => o.ForwardDefaultSelector = ctx =>
                UsesJwt(ctx.Request) ? JwtBearerDefaults.AuthenticationScheme : CookieScheme)
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                // Plain HTTP in compose (Q74).
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromHours(1);
                o.SlidingExpiration = true;
                // fetch must never follow a redirect into the login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            })
            .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, o =>
            {
                if (string.IsNullOrWhiteSpace(options.MetadataAddress)) o.Authority = options.Issuer;
                else o.MetadataAddress = options.MetadataAddress;
                o.RequireHttpsMetadata = options.RequireHttpsMetadata;
                o.ClientId = options.UiClientId;
                o.ResponseType = "code";
                o.UsePkce = true;
                o.SaveTokens = true;
                o.MapInboundClaims = false;
                o.GetClaimsFromUserInfoEndpoint = false;
                o.Scope.Clear();
                o.Scope.Add("openid");
                // The realm has no "profile" client scope (Keycloak rejects unknown scopes); the client's own mappers add the claims.
                o.SignInScheme = CookieScheme;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    NameClaimType = SiteSenseClaims.NameClaim,
                    RoleClaimType = SiteSenseClaims.RoleClaim,
                };
                // Plain HTTP in compose: SameSite=None would need Secure and the cookies would be dropped (Q74).
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.NonceCookie.SameSite = SameSiteMode.Lax;
                o.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events.OnTokenValidated = ctx =>
                {
                    CheckTenant(ctx.Principal, ctx.Fail);
                    return Task.CompletedTask;
                };
            })
            .AddJwtBearer(o =>
            {
                // Keys are downloaded once, cached and refreshed by the handler; signatures are checked locally.
                if (string.IsNullOrWhiteSpace(options.MetadataAddress)) o.Authority = options.Issuer;
                else o.MetadataAddress = options.MetadataAddress;
                o.RequireHttpsMetadata = options.RequireHttpsMetadata;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    NameClaimType = SiteSenseClaims.NameClaim,
                    RoleClaimType = SiteSenseClaims.RoleClaim,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
                o.Events = new JwtBearerEvents
                {
                    // One tenant per caller (IDN-02); a value the store would refuse is a bad token, not a 500.
                    OnTokenValidated = ctx =>
                    {
                        CheckTenant(ctx.Principal, ctx.Fail);
                        return Task.CompletedTask;
                    },
                    // Browsers cannot send headers on WebSockets; the query token is read for the hub path only.
                    OnMessageReceived = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments(SiteHub.RealtimePath, StringComparison.OrdinalIgnoreCase)
                            && string.IsNullOrEmpty(ctx.Request.Headers.Authorization)
                            && ctx.Request.Query.TryGetValue("access_token", out var token))
                            ctx.Token = token;
                        return Task.CompletedTask;
                    },
                };
            });

        // Every endpoint needs a valid token unless it says AllowAnonymous (Q80).
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            // Named policies only (IDN-03). Admin does not see individuals (Q09); Viewer only aggregates (PRV-03).
            .AddPolicy(SiteSensePolicies.CanManage, p => p.RequireAuthenticatedUser().RequireRole(SiteSensePolicies.AdminRole))
            .AddPolicy(SiteSensePolicies.CanSeeIndividuals, p => p.RequireAuthenticatedUser().RequireRole(SiteSensePolicies.SafetyRole))
            .AddPolicy(SiteSensePolicies.CanSeeAggregates, p => p.RequireAuthenticatedUser()
                .RequireRole(SiteSensePolicies.AdminRole, SiteSensePolicies.SafetyRole, SiteSensePolicies.ViewerRole))
            // Keycloak sends all scopes in one space-separated "scope" claim.
            .AddPolicy(SiteSensePolicies.CanWritePositions, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                ctx.User.FindAll(SiteSensePolicies.ScopeClaim)
                    .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    .Contains(SiteSensePolicies.PositionsWriteScope, StringComparer.Ordinal)))
            // The flag check backs up the 404 gate in Program.cs.
            .AddPolicy(SiteSensePolicies.TestMode, p => p.RequireAuthenticatedUser().RequireRole(SiteSensePolicies.AdminRole)
                .RequireAssertion(_ => testMode));
    }

    private static void CheckTenant(ClaimsPrincipal? principal, Action<string> fail)
    {
        var tenants = principal?.FindAll(SiteSenseClaims.TenantIdClaim).ToList() ?? [];
        if (tenants.Count != 1 || !FileDocumentStore.IsValidName(tenants[0].Value))
            fail("The token must carry exactly one valid tenant_id claim.");
    }
}
