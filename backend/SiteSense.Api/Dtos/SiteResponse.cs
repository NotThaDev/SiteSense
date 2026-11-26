using SiteSense.Api.Models;

namespace SiteSense.Api.Dtos;

public sealed record SiteResponse(string SiteId, string Name, string TimeZone, string MapFile, NavigationSettings Navigation)
{
    public static SiteResponse From(Site s) => new(s.Id, s.Name, s.TimeZone, s.MapFile, s.Navigation);
}

public sealed record SiteListResponse(IReadOnlyList<SiteResponse> Items);
