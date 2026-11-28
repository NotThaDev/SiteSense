using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;

namespace SiteSense.Api.Controllers;

// Safety only (CanSeeIndividuals): a track is an individual's movement. No coordinates in logs.
[ApiController]
[Route("api/v1/devices/{deviceId}/track")]
[Authorize(Policy = SiteSensePolicies.CanSeeIndividuals)]
public sealed class TrackController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<TrackResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string deviceId, [FromQuery] TrackRequest request, CancellationToken ct)
    {
        var from = request.FromMs!.Value;
        var to = request.ToMs!.Value;
        if (from >= to)
            return ValidationProblem(Error(nameof(request.FromMs), "fromMs must be less than toMs."));
        long? cursor = null;
        if (request.Cursor is not null)
        {
            if (!long.TryParse(request.Cursor, out var c) || c < from || c >= to)
                return ValidationProblem(Error(nameof(request.Cursor), "cursor is not valid for this range."));
            cursor = c;
        }

        var tenantId = User.TenantId();
        if (await Device.GetAsync(tenantId, deviceId, ct) is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");

        // shortcut: reads every hour file in the range, then cuts to limit; fine for one-hour ranges, upgrade with SS-11
        var points = await TrackPoint.ReadAsync(tenantId, deviceId, cursor is { } cur ? cur + 1 : from, to, ct);
        var page = points.Take(request.Limit).Select(TrackPointResponse.From).ToList();
        var next = points.Count > request.Limit ? page[^1].TimestampMs.ToString() : null;
        return Ok(new TrackResponse(page, next));
    }

    private static ModelStateDictionary Error(string key, string message)
    {
        var state = new ModelStateDictionary();
        state.AddModelError(key, message);
        return state;
    }
}
