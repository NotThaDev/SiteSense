using Microsoft.AspNetCore.SignalR;

namespace SiteSense.Api.Realtime;

// The only class that touches IHubContext (architecture skill).
public sealed class SignalRSender(IHubContext<SiteHub> hub) : IEventSender
{
    public async Task SendAsync(string tenantId, TenantEvent evt, CancellationToken ct)
    {
        if (EventAudience.PolicyFor(evt.Type) is not { } policy) return;
        await hub.Clients.Group(SiteHub.GroupName(tenantId, evt.SiteId, policy)).SendAsync(evt.Type, evt, ct);
        // The tenant-wide Alarms page also follows alarms of every site.
        if (evt.Type is EventTypes.AlertRaised or EventTypes.AlertUpdated)
            await hub.Clients.Group(SiteHub.TenantGroupName(tenantId, policy)).SendAsync(evt.Type, evt, ct);
    }
}
