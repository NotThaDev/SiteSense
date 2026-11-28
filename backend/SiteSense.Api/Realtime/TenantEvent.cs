using System.Text.Json;
using System.Text.Json.Serialization;
using SiteSense.Api.Store;

namespace SiteSense.Api.Realtime;

public static class EventTypes
{
    public const string PositionUpdated = "PositionUpdated";
    public const string OccupancyChanged = "OccupancyChanged";
    public const string AlertRaised = "AlertRaised";
    public const string AlertUpdated = "AlertUpdated";
}

// Payload of PositionUpdated (Q115): a track point plus the device. AreaId is the point's area (SS-25); null when the site has no map.
public sealed record PositionUpdatedData(string DeviceId, long TimestampMs, double Lat, double Lon, string? AreaId);

// Payload of AlertRaised (Q116): the alert's fields, state is always Active at creation.
public sealed record AlertRaisedData(string AlertId, string Type, long EventTimeMs, string? DeviceId, string? AreaId, bool Late);

// Payload of AlertUpdated: sent once per state change (only Active to NotActive exists).
public sealed record AlertUpdatedData(string AlertId, string State, long EndTimeMs);

// shortcut: TimestampMs carries the seq (x100), so FileAppendLog's hour files hold 36,000 seqs each, not dates (Q105).
public sealed record TenantEvent(long Seq, string Type, string SiteId, long AtMs, JsonElement Data) : ILogRecord
{
    public const long SeqScale = 100;
    [JsonIgnore] public long TimestampMs => Seq * SeqScale;
}
