using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SiteSense.Tests.Api.Auth;

// Signs real JWTs like Keycloak does, with a key that exists only in the test run (Q77).
public static class TestTokens
{
    public const string Issuer = "http://localhost:8081/realms/sitesense";
    public const string Audience = "sitesense-api";
    public static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    // tenant: a string, a string[] (several claims) or null (no claim).
    public static string Create(object? tenant = null, string[]? roles = null, string issuer = Issuer,
        string audience = Audience, SecurityKey? key = null, DateTime? expires = null, bool noTenant = false)
    {
        var claims = new Dictionary<string, object>();
        if (!noTenant) claims["tenant_id"] = tenant ?? "tenant-a";
        if (roles is { Length: > 0 }) claims["roles"] = roles;
        // The handler checks lifetime against the real clock, so the token must too.
        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = exp.AddMinutes(-10),
            NotBefore = exp.AddMinutes(-10),
            Expires = exp,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
