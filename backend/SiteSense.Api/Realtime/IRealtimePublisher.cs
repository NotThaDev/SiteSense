namespace SiteSense.Api.Realtime;

public interface IRealtimePublisher
{
    Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct);
}

public sealed record EventPage(IReadOnlyList<TenantEvent> Items, long LastSeq, bool HasMore);
