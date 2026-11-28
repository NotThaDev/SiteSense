using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using SiteSense.Api.Auth;
using SiteSense.Api.Models;
using SiteSense.Api.Store;

namespace SiteSense.Api.Realtime;

[Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
public sealed class SiteHub(IAuthorizationService auth) : Hub
{
    public const string RealtimePath = "/api/v1/realtime";

    // Q108: one group per audience policy; a valid site name has no ':', so a group name cannot be forged.
    public static string GroupName(string tenantId, string siteId, string policy) => $"tenant:{tenantId}:site:{siteId}:audience:{policy}";

    // Every site of the tenant (the Alarms page). "all" cannot be a site name, so it never collides with a site group.
    public static string TenantGroupName(string tenantId, string policy) => $"tenant:{tenantId}:all:audience:{policy}";

    public async Task JoinTenant()
    {
        var user = Context.User!;
        var tenantId = user.TenantId();
        foreach (var policy in await EventAudience.PassedPoliciesAsync(auth, user))
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroupName(tenantId, policy), Context.ConnectionAborted);
    }

    public async Task LeaveTenant()
    {
        var tenantId = Context.User!.TenantId();
        foreach (var policy in EventAudience.Policies)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, TenantGroupName(tenantId, policy), Context.ConnectionAborted);
    }

    public async Task JoinSite(string siteId)
    {
        var user = Context.User!;
        var tenantId = user.TenantId();
        // Invalid name, unknown site and another tenant's site all give the same answer (IDN-02).
        if (!FileDocumentStore.IsValidName(siteId) || await Site.GetAsync(tenantId, siteId, Context.ConnectionAborted) is null)
            throw new HubException("Site not found");
        foreach (var policy in await EventAudience.PassedPoliciesAsync(auth, user))
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(tenantId, siteId, policy), Context.ConnectionAborted);
    }

    public async Task LeaveSite(string siteId)
    {
        if (!FileDocumentStore.IsValidName(siteId)) return; // nothing can be joined under an invalid name
        var tenantId = Context.User!.TenantId();
        foreach (var policy in EventAudience.Policies)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(tenantId, siteId, policy), Context.ConnectionAborted);
    }
}
