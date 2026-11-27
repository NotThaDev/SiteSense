using Microsoft.AspNetCore.SignalR;

namespace SiteSense.Api.Realtime;

// The only class that touches IHubContext (architecture skill).
public sealed class SignalRSender(IHubContext<SiteHub> hub) : IEventSender
{
    public Task SendAsync(string tenantId, TenantEvent evt, CancellationToken ct) =>
        EventAudience.PolicyFor(evt.Type) is { } policy
            ? hub.Clients.Group(SiteHub.GroupName(tenantId, evt.SiteId, policy)).SendAsync(evt.Type, evt, ct)
            : Task.CompletedTask;
}
