using System.Security.Cryptography;
using System.Text;
using SiteSense.Api.Helpers;
using SiteSense.Api.Store;

namespace SiteSense.Api.Alarms;

// Contract strings (Q116): stored and sent as they are.
public static class AlertTypes
{
    public const string AreaIntrusion = "AreaIntrusion";
    public const string ZoneAnomaly = "ZoneAnomaly";
    public const string SuddenImmobility = "SuddenImmobility";
    public const string DeviceSilent = "DeviceSilent";
}

public static class AlertStates
{
    public const string Active = "Active";
    public const string NotActive = "NotActive";
}

public sealed class Alert : IDocument
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }
    public long Version { get; set; }
    public required string SiteId { get; init; }
    public required string Type { get; init; }
    public required long EventTimeMs { get; init; }
    public string? DeviceId { get; init; }
    public string? AreaId { get; init; }
    public required bool Late { get; init; }
    public required string State { get; set; }
    public long? EndTimeMs { get; set; }

    // Built only from the alarm's inputs, so a retry or a replayed batch gives the same id (JOB-02, EVT-03).
    // Lowercase: the store escapes uppercase letters in file names (Q76).
    public static string IdFor(string type, string siteId, string? deviceId, string? areaId, long eventTimeMs)
    {
        var key = $"{type}|{siteId}|{deviceId}|{areaId}|{eventTimeMs}"; // null becomes an empty part
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    // Null when the alert already exists: the store's version check runs under its per-document lock.
    public static async Task<Alert?> CreateAsync(string tenantId, Alert alert, CancellationToken ct)
    {
        try
        {
            return await SiteSenseContext.Store.SaveAsync(tenantId, alert, ct);
        }
        catch (ConcurrencyException)
        {
            return null;
        }
    }

    // alert.Version must be the stored version, else ConcurrencyException.
    public static Task<Alert> UpdateAsync(string tenantId, Alert alert, CancellationToken ct) =>
        SiteSenseContext.Store.SaveAsync(tenantId, alert, ct);

    // Null for a missing alert, another tenant's alert, or an id the store would reject.
    public static Task<Alert?> GetAsync(string tenantId, string id, CancellationToken ct) =>
        FileDocumentStore.IsValidName(id) ? SiteSenseContext.Store.GetAsync<Alert>(tenantId, id, ct) : Task.FromResult<Alert?>(null);

    // siteId null = every site of the tenant.
    // shortcut: reads every alert of the tenant and filters the site in memory; per-site or per-day files when the list grows.
    public static async Task<IReadOnlyList<Alert>> ListAsync(string tenantId, string? siteId, CancellationToken ct)
    {
        var all = await SiteSenseContext.Store.ListAsync<Alert>(tenantId, ct);
        return siteId is null ? all.ToList() : all.Where(a => a.SiteId == siteId).ToList();
    }

    // Exact match on deviceId and areaId: null matches only an alert with no device or no area.
    public static async Task<IReadOnlyList<Alert>> ListActiveAsync(string tenantId, string siteId, string? deviceId, string? areaId, CancellationToken ct) =>
        (await ListAsync(tenantId, siteId, ct))
            .Where(a => a.State == AlertStates.Active && a.DeviceId == deviceId && a.AreaId == areaId)
            .ToList();

    // NotActive AreaIntrusion alarms of the device in the area that ended 0..cooldownMs before timeMs; the latest end wins.
    // shortcut: filtered in memory like ListActiveAsync; same upgrade path.
    public static async Task<Alert?> FindReactivatableAsync(string tenantId, string siteId, string deviceId, string areaId, long timeMs, long cooldownMs, CancellationToken ct) =>
        (await ListAsync(tenantId, siteId, ct))
            .Where(a => a.Type == AlertTypes.AreaIntrusion && a.State == AlertStates.NotActive && a.DeviceId == deviceId && a.AreaId == areaId
                && a.EndTimeMs is { } end && timeMs - end >= 0 && timeMs - end <= cooldownMs)
            .MaxBy(a => a.EndTimeMs);
}
