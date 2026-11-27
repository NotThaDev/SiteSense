namespace SiteSense.Api.Realtime;

// Sends a saved event to the connected clients of a tenant; PR 2 replaces the null sender with SignalR.
public interface IEventSender
{
    Task SendAsync(string tenantId, TenantEvent evt, CancellationToken ct);
}

public sealed class NullEventSender : IEventSender
{
    public Task SendAsync(string tenantId, TenantEvent evt, CancellationToken ct) => Task.CompletedTask;
}
