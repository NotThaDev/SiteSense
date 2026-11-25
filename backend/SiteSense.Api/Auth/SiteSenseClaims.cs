using System.Security.Claims;

namespace SiteSense.Api.Auth;

public static class SiteSenseClaims
{
    public const string TenantIdClaim = "tenant_id";
    public const string RoleClaim = "roles";
    public const string NameClaim = "preferred_username";

    // AuthSetup rejects tokens without exactly one valid tenant_id, so an authenticated user always has one.
    public static string TenantId(this ClaimsPrincipal user) =>
        user.FindFirstValue(TenantIdClaim) ?? throw new InvalidOperationException("Authenticated user without tenant_id.");
}
