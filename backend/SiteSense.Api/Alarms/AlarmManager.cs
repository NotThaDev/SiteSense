using Microsoft.Extensions.Options;
using SiteSense.Api.Helpers;
using SiteSense.Api.Realtime;
using SiteSense.Api.Store;

namespace SiteSense.Api.Alarms;

// The one entry point for alarms: a producer decides, this class saves once, then publishes once.
public sealed class AlarmManager(IRealtimePublisher publisher, ILogger<AlarmManager> logger, IOptions<AlarmOptions> options)
{
    // False when the same alarm already exists: nothing is saved or published again (JOB-02, EVT-03).
    public async Task<bool> NotifyAlarmAsync(string tenantId, string siteId, string type, string? deviceId, string? areaId, long eventTimeMs, bool replay, CancellationToken ct)
    {
        // The one place that decides late (Q33): a replay, or detected more than LateAfterMs after the event. The id ignores it.
        var late = replay || SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds() - eventTimeMs > options.Value.LateAfterMs;
        var id = Alert.IdFor(type, siteId, deviceId, areaId, eventTimeMs);
        var saved = await Alert.CreateAsync(tenantId, new Alert
        {
            Id = id,
            TenantId = tenantId,
            SiteId = siteId,
            Type = type,
            EventTimeMs = eventTimeMs,
            DeviceId = deviceId,
            AreaId = areaId,
            Late = late,
            State = AlertStates.Active,
        }, ct);
        if (saved is null) return false;

        await PublishAsync(tenantId, siteId, id, EventTypes.AlertRaised, new AlertRaisedData(id, type, eventTimeMs, deviceId, areaId, late), ct);
        return true;
    }

    // False for an unknown or already NotActive alert: nothing is saved or published (idempotent).
    public async Task<bool> DeactivateAlarmAsync(string tenantId, string alertId, long endTimeMs, CancellationToken ct)
    {
        Alert? alert;
        for (var attempt = 1; ; attempt++)
        {
            alert = await Alert.GetAsync(tenantId, alertId, ct);
            if (alert is null || alert.State != AlertStates.Active) return false;
            alert.State = AlertStates.NotActive;
            alert.EndTimeMs = endTimeMs;
            try
            {
                await Alert.UpdateAsync(tenantId, alert, ct);
                break;
            }
            // Reload once: another writer may have ended the alert already, and the state check above then sees it.
            catch (ConcurrencyException) when (attempt == 1) { }
        }

        await PublishAsync(tenantId, alert.SiteId, alertId, EventTypes.AlertUpdated, new AlertUpdatedData(alertId, AlertStates.NotActive, endTimeMs), ct);
        return true;
    }

    // The alert is already saved when this runs: a failed publish is logged, never thrown (at most once, as Q115). No coordinates in the log (PRV).
    private async Task PublishAsync<TData>(string tenantId, string siteId, string alertId, string type, TData data, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(tenantId, siteId, type, data, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Type} not published for alert {AlertId}", type, alertId);
        }
    }
}
