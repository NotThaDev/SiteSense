using System.Text.Json;
using System.Text.Json.Serialization;
using SiteSense.Api.Store;

namespace SiteSense.Api.Realtime;

public static class EventTypes
{
    public const string PositionUpdated = "PositionUpdated";
    public const string OccupancyChanged = "OccupancyChanged";
    public const string AlertRaised = "AlertRaised";
}

// shortcut: TimestampMs carries the seq (x100), so FileAppendLog's hour files hold 36,000 seqs each, not dates (Q105).
public sealed record TenantEvent(long Seq, string Type, string SiteId, long AtMs, JsonElement Data) : ILogRecord
{
    public const long SeqScale = 100;
    [JsonIgnore] public long TimestampMs => Seq * SeqScale;
}
