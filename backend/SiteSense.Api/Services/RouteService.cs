using System.Collections.Concurrent;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;
using SiteSense.Domain.Geometry;
using SiteSense.Domain.Navigation;

namespace SiteSense.Api.Services;

public sealed record RouteOutcome(RouteStatus Status, double[][] Coordinates, double DistanceM, double DurationS, string? Detail);

// Finds routes on a graph cached per (tenant, site); a new map or site version replaces the entry (Q86).
public sealed class RouteService
{
    private sealed record CacheEntry(long MapVersion, long SiteVersion, RouteGraph Graph, LocalProjection Projection);

    private readonly ConcurrentDictionary<(string Tenant, string Site), Lazy<CacheEntry>> _cache = new();

    // Null when the site or its map does not exist.
    public async Task<RouteOutcome?> FindAsync(
        string tenantId, string siteId, (double Lat, double Lon) from, (double Lat, double Lon) to, CancellationToken ct)
    {
        var site = await Site.GetAsync(tenantId, siteId, ct);
        if (site is null) return null;
        var map = await SiteMap.GetAsync(tenantId, siteId, ct);
        if (map is null) return null;

        var entry = GetEntry(tenantId, siteId, map, site);
        var result = RouteFinder.Find(entry.Graph, entry.Projection.ToLocal(from.Lon, from.Lat), entry.Projection.ToLocal(to.Lon, to.Lat),
            site.Navigation.MaxSnapDistanceM, site.Navigation.WalkingSpeedMps);
        var coordinates = result.Points.Select(p => entry.Projection.ToWgs84(p.X, p.Y)).Select(p => new[] { p.Lon, p.Lat }).ToArray();
        return new RouteOutcome(result.Status, coordinates, result.DistanceM, result.DurationS, result.Detail);
    }

    private CacheEntry GetEntry(string tenantId, string siteId, SiteMap map, Site site)
    {
        Lazy<CacheEntry> Create() => new(() =>
        {
            var (graph, projection) = map.BuildRouteContext(site.Navigation);
            return new CacheEntry(map.Version, site.Version, graph, projection);
        });

        // Never replace an entry that is newer than this request (a slow request must not roll the cache back).
        bool Same(CacheEntry e) => e.MapVersion == map.Version && e.SiteVersion == site.Version;
        var entry = _cache.AddOrUpdate((tenantId, siteId), _ => Create(), (_, old) =>
            Same(old.Value) || old.Value.MapVersion > map.Version || old.Value.SiteVersion > site.Version ? old : Create()).Value;
        // An older request that met a newer entry builds its own graph and leaves the cache alone.
        return Same(entry) ? entry : Create().Value;
    }
}
