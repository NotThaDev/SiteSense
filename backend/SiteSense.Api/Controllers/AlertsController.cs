using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Alarms;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/alerts")]
public sealed class AlertsController : ControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;

    // Newest first (eventTimeMs, then alertId). fromMs and toMs are both inclusive. Same audience as AlertRaised (Q36).
    [HttpGet]
    [Authorize(Policy = SiteSensePolicies.CanSeeIndividuals)]
    [ProducesResponseType<AlertListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(string siteId, [FromQuery] long? fromMs, [FromQuery] long? toMs,
        [FromQuery] string? type, [FromQuery] string? state, [FromQuery] int limit = DefaultLimit, [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var tenantId = User.TenantId();
        if (await Site.GetAsync(tenantId, siteId, ct) is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Site not found");

        if (limit is < 1 or > MaxLimit)
            ModelState.AddModelError(nameof(limit), $"limit must be between 1 and {MaxLimit}.");
        if (type is not null && type is not (AlertTypes.AreaIntrusion or AlertTypes.ZoneAnomaly or AlertTypes.SuddenImmobility or AlertTypes.DeviceSilent))
            ModelState.AddModelError(nameof(type), "type is not a known alert type.");
        if (state is not null && state is not (AlertStates.Active or AlertStates.NotActive))
            ModelState.AddModelError(nameof(state), "state must be Active or NotActive.");
        (long Ms, string Id)? after = null;
        if (cursor is not null)
        {
            after = DecodeCursor(cursor);
            if (after is null) ModelState.AddModelError(nameof(cursor), "cursor is not valid.");
        }
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // shortcut: the whole site is read, then filtered and paged in memory (see Alert.ListAsync).
        var matching = (await Alert.ListAsync(tenantId, siteId, ct))
            .Where(a => (fromMs is null || a.EventTimeMs >= fromMs) && (toMs is null || a.EventTimeMs <= toMs)
                && (type is null || a.Type == type) && (state is null || a.State == state))
            .OrderByDescending(a => a.EventTimeMs).ThenByDescending(a => a.Id, StringComparer.Ordinal)
            .ToList();
        // Strictly after the cursor position in this order (newest first).
        var rest = after is { } c
            ? matching.Where(a => a.EventTimeMs < c.Ms || (a.EventTimeMs == c.Ms && string.CompareOrdinal(a.Id, c.Id) < 0)).ToList()
            : matching;

        var page = rest.Take(limit).ToList();
        var nextCursor = rest.Count > limit ? EncodeCursor(page[^1]) : null;
        return Ok(new AlertListResponse(page.Select(AlertResponse.From).ToList(), nextCursor));
    }

    // Opaque to clients: base64url of "eventTimeMs|alertId".
    private static string EncodeCursor(Alert alert) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes($"{alert.EventTimeMs}|{alert.Id}"));

    private static (long Ms, string Id)? DecodeCursor(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|', 2);
            if (parts.Length == 2 && parts[1].Length > 0 && long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
                return (ms, parts[1]);
        }
        catch (FormatException)
        {
            // Not base64url: fall through to null (a 400 for the caller).
        }
        return null;
    }
}
