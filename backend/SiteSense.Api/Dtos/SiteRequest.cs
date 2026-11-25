using System.ComponentModel.DataAnnotations;
using SiteSense.Api.Models;
using SiteSense.Api.Store;

namespace SiteSense.Api.Dtos;

public sealed record NavigationRequest(
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)] double WalkingSpeedMps,
    [Range(0.0, 1.0, MinimumIsExclusive = true)] double UnpavedSpeedFactor,
    [Range(0.0, 1.0, MinimumIsExclusive = true)] double StepsSpeedFactor,
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)] double ConnectToleranceM,
    [Range(0.0, double.MaxValue, MinimumIsExclusive = true)] double MaxSnapDistanceM);

// The site JSON (Q82: full check before anything is stored). siteId is optional: null on POST = the server makes one; on PUT the URL is the id (Q43).
public sealed record SiteRequest(
    [Required] string Name,
    [Required] string TimeZone,
    [Required] string MapFile,
    [Required] NavigationRequest Navigation,
    string? SiteId = null) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (SiteId is not null && !FileDocumentStore.IsValidName(SiteId))
            yield return new ValidationResult("siteId may contain only letters, digits, '-' and '_' (1-128 characters).", [nameof(SiteId)]);
        if (TimeZone is not null && !TimeZoneInfo.TryFindSystemTimeZoneById(TimeZone, out _))
            yield return new ValidationResult($"timeZone '{TimeZone}' is not a known time zone id.", [nameof(TimeZone)]);
    }

    public Site ToSite(string tenantId, string siteId) => new() { Id = siteId, TenantId = tenantId, Name = Name, TimeZone = TimeZone, MapFile = MapFile,
        Navigation = new(Navigation.WalkingSpeedMps, Navigation.UnpavedSpeedFactor, Navigation.StepsSpeedFactor, Navigation.ConnectToleranceM, Navigation.MaxSnapDistanceM) };
}
