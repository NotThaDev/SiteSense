using SiteSense.Api.Alarms;

namespace SiteSense.Api.Dtos;

public sealed record AlertResponse(string AlertId, string Type, long EventTimeMs, string? DeviceId, string? AreaId, bool Late, string State, long? EndTimeMs)
{
    public static AlertResponse From(Alert alert) =>
        new(alert.Id, alert.Type, alert.EventTimeMs, alert.DeviceId, alert.AreaId, alert.Late, alert.State, alert.EndTimeMs);
}

public sealed record AlertListResponse(IReadOnlyList<AlertResponse> Items, string? NextCursor);
