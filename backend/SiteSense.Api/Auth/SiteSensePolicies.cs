namespace SiteSense.Api.Auth;

// Named policies (IDN-03): endpoints use RequireAuthorization(SiteSensePolicies.X), never role checks.
public static class SiteSensePolicies
{
    public const string CanManage = "CanManage";
    public const string CanSeeIndividuals = "CanSeeIndividuals";
    public const string CanSeeAggregates = "CanSeeAggregates";
    public const string CanWritePositions = "CanWritePositions";

    public const string AdminRole = "Admin";
    public const string SafetyRole = "Safety";
    public const string ViewerRole = "Viewer";

    public const string ScopeClaim = "scope";
    public const string PositionsWriteScope = "positions:write";
}
