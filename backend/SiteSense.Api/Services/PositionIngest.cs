using SiteSense.Api.Dtos;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;

namespace SiteSense.Api.Services;

// Batch ingest (TRK-01, PRV-02): device check, perimeter drop before any write, write-once append. Processing reads the raw log later (SS-23).
public static class PositionIngest
{
    private const double BoundaryToleranceM = 1e-6;

    // Null when the device is not registered in this tenant (Q60: 404, nothing stored).
    public static async Task<PositionBatchResponse?> IngestAsync(string tenantId, string deviceId, IReadOnlyList<Position> positions, ILogger logger, CancellationToken ct)
    {
        var device = await Device.GetAsync(tenantId, deviceId, ct);
        if (device is null) return null;

        var map = await SiteMap.GetAsync(tenantId, device.SiteId, ct);
        var perimeter = map?.Areas.FirstOrDefault(a => a.AreaType == "perimeter");

        var inside = perimeter is null ? [] : PositionsInside(perimeter, positions);
        var stored = inside.Count == 0 ? 0 : await Position.AppendNewAsync(tenantId, deviceId, inside, ct);
        var outside = positions.Count - inside.Count;
        var duplicates = inside.Count - stored;

        logger.LogInformation("Position batch for {DeviceId}: {Received} received, {Stored} stored, {Duplicates} duplicates, {OutsidePerimeter} outside the perimeter",
            deviceId, positions.Count, stored, duplicates, outside);
        return new PositionBatchResponse(positions.Count, stored, duplicates, outside);
    }

    // A point on the boundary is kept.
    private static List<Position> PositionsInside(MapArea perimeter, IReadOnlyList<Position> positions)
    {
        var projection = LocalProjection.FromCentroid(perimeter.Coordinates[0].Select(c => (Lon: c[0], Lat: c[1])).ToList());
        var rings = perimeter.Coordinates
            .Select(r => (IReadOnlyList<(double X, double Y)>)r.Select(c => projection.ToLocal(c[0], c[1])).ToList())
            .ToList();
        return positions
            .Where(p => PlanarGeometry.Locate(projection.ToLocal(p.Lon, p.Lat), rings, BoundaryToleranceM) != PointLocation.Outside)
            .ToList();
    }
}
