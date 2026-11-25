using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SiteSense.Api.Store;

namespace SiteSense.Api.Auth;

public static class AuthSetup
{
    public static void AddSiteSenseAuth(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.Issuer)) failures.Add("Auth:Issuer is required.");
        if (string.IsNullOrWhiteSpace(options.Audience)) failures.Add("Auth:Audience is required.");
        if (failures.Count > 0)
            throw new OptionsValidationException(AuthOptions.Section, typeof(AuthOptions), failures);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // Keys are downloaded once, cached and refreshed by the handler; signatures are checked locally.
                if (string.IsNullOrWhiteSpace(options.MetadataAddress)) o.Authority = options.Issuer;
                else o.MetadataAddress = options.MetadataAddress;
                o.RequireHttpsMetadata = false; // Keycloak runs on plain HTTP in compose (Q74).
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
                        var tenants = ctx.Principal?.FindAll(SiteSenseClaims.TenantIdClaim).ToList() ?? [];
                        if (tenants.Count != 1 || !FileDocumentStore.IsValidName(tenants[0].Value))
                            ctx.Fail("The token must carry exactly one valid tenant_id claim.");
                        return Task.CompletedTask;
                    },
                };
            });

        // Every endpoint needs a valid token unless it says AllowAnonymous (Q80).
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }
}
