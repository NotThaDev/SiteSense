namespace SiteSense.Api.Auth;

public sealed class AuthOptions
{
    public const string Section = "Auth";

    // Exact `iss` of Keycloak tokens; fixed by KC_HOSTNAME (Q74).
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    // Set in compose to the internal Keycloak name; empty on the host, where the issuer is reachable (Q02).
    public string? MetadataAddress { get; set; }
    // Secure by default; the template and compose turn it off for the plain-HTTP local Keycloak (Q74).
    public bool RequireHttpsMetadata { get; set; } = true;
    // Public client used by the browser login (BFF); no secret, PKCE protects the code.
    public string UiClientId { get; set; } = "sitesense-ui";
}
