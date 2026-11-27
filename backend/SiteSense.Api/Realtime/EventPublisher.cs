using System.Collections.Concurrent;
using System.Text.Json;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Realtime;

public sealed class EventPublisher(IEventSender sender, ILogger<EventPublisher> logger) : IRealtimePublisher
{
    public const string LogKey = "events";
    public const int PageSize = 1000;
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(5);

    // One process (Q01): a per-tenant lock and an in-memory last seq are enough (JDB-04).
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, long> _lastSeq = new();

    public async Task<TenantEvent> PublishAsync<TData>(string tenantId, string siteId, string type, TData data, CancellationToken ct)
    {
        var element = JsonSerializer.SerializeToElement(data, StorePaths.Json);
        var gate = _locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var seq = await LastSeqAsync(tenantId, ct) + 1;
            var evt = new TenantEvent(seq, type, siteId, SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds(), element);
            try { await SiteSenseContext.Log.AppendAsync(tenantId, LogKey, [evt], ct); } // saved before sent (RTM-03)
            catch
            {
                // The record may be on disk anyway; reload the last seq from the log so it is never reused.
                _lastSeq.TryRemove(tenantId, out _);
                throw;
            }
            _lastSeq[tenantId] = seq;
            // Inside the lock so clients get events in seq order; a failed send is covered by GET /events.
            using var sendCts = new CancellationTokenSource(SendTimeout);
            try { await sender.SendAsync(tenantId, evt, sendCts.Token); }
            catch (Exception ex) { logger.LogWarning(ex, "Event {Type} seq {Seq} saved but not sent", type, seq); }
            return evt;
        }
        finally { gate.Release(); }
    }

    public async Task<EventPage> ReadAfterAsync(string tenantId, long after, IReadOnlyCollection<string> passedPolicies, CancellationToken ct)
    {
        // Loaded under the gate only when not cached, so it cannot race the first publish.
        var last = _lastSeq.TryGetValue(tenantId, out var cached) ? cached : await LoadLastSeqAsync(tenantId, ct);
        var to = Math.Min(after + PageSize, last);
        if (to <= after) return new EventPage([], after, false);
        var records = await SiteSenseContext.Log.ReadRangeAsync<TenantEvent>(tenantId, LogKey, (after + 1) * TenantEvent.SeqScale, (to + 1) * TenantEvent.SeqScale, ct);
        var items = records.Where(e => EventAudience.CanSee(e.Type, passedPolicies)).ToList();
        return new EventPage(items, to, to < last);
    }

    private async Task<long> LoadLastSeqAsync(string tenantId, CancellationToken ct)
    {
        var gate = _locks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await LastSeqAsync(tenantId, ct); }
        finally { gate.Release(); }
    }

    private async Task<long> LastSeqAsync(string tenantId, CancellationToken ct) // call with the gate held
    {
        if (_lastSeq.TryGetValue(tenantId, out var cached)) return cached;
        var last = await SiteSenseContext.Log.ReadLastAsync<TenantEvent>(tenantId, LogKey, ct);
        var seq = last?.Seq ?? 0;
        _lastSeq[tenantId] = seq;
        return seq;
    }
}
