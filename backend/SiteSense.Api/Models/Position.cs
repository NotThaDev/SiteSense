using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Models;

public sealed record Position(string DeviceId, long TimestampMs, double Lat, double Lon, double AccuracyM) : ILogRecord
{
    public static Task AppendAsync(string tenantId, string deviceId, IReadOnlyList<Position> positions, CancellationToken ct)
    {
        if (positions.Any(p => p.DeviceId != deviceId))
            throw new ArgumentException("Every position must belong to the device it is appended under.", nameof(positions));
        return SiteSenseContext.Log.AppendAsync(tenantId, deviceId, positions, ct);
    }

    // Empty for an id the store would reject.
    public static Task<IReadOnlyList<Position>> ReadAsync(string tenantId, string deviceId, long fromMs, long toMs, CancellationToken ct) =>
        FileDocumentStore.IsValidName(deviceId)
            ? SiteSenseContext.Log.ReadRangeAsync<Position>(tenantId, deviceId, fromMs, toMs, ct)
            : Task.FromResult<IReadOnlyList<Position>>([]);
}
