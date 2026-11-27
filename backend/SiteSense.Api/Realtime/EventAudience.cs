using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SiteSense.Api.Auth;

namespace SiteSense.Api.Realtime;

// Who sees an event type is a named policy, never a role string (IDN-03, Q108).
public static class EventAudience
{
    private static readonly Dictionary<string, string> PolicyByType = new()
    {
        [EventTypes.PositionUpdated] = SiteSensePolicies.CanSeeIndividuals,
        [EventTypes.AlertRaised] = SiteSensePolicies.CanSeeIndividuals, // Q36
        [EventTypes.OccupancyChanged] = SiteSensePolicies.CanSeeAggregates,
    };

    public static IReadOnlyList<string> Policies { get; } = [SiteSensePolicies.CanSeeIndividuals, SiteSensePolicies.CanSeeAggregates];

    public static string? PolicyFor(string type) => PolicyByType.GetValueOrDefault(type);

    public static bool CanSee(string type, IReadOnlyCollection<string> passedPolicies) =>
        PolicyFor(type) is { } policy && passedPolicies.Contains(policy);

    public static async Task<IReadOnlyList<string>> PassedPoliciesAsync(IAuthorizationService auth, ClaimsPrincipal user)
    {
        var passed = new List<string>();
        foreach (var policy in Policies)
            if ((await auth.AuthorizeAsync(user, policy)).Succeeded) passed.Add(policy);
        return passed;
    }
}
