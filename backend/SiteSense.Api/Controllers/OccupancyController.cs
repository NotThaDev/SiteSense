using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Helpers;
using SiteSense.Api.Models;
using SiteSense.Api.Services;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/occupancy")]
public sealed class OccupancyController(OccupancyService occupancy) : ControllerBase
{
    // Aggregates only, so Viewer may read it (PRV-03). Unknown site or another tenant's site: 404.
    [HttpGet]
    [Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
    [ProducesResponseType<OccupancyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string siteId, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        if (await Site.GetAsync(tenantId, siteId, ct) is null) return Problem(statusCode: StatusCodes.Status404NotFound, title: "Site not found");
        var nowMs = SiteSenseContext.Time.GetUtcNow().ToUnixTimeMilliseconds();
        return Ok(new OccupancyResponse(nowMs, await occupancy.ComputeAsync(tenantId, siteId, nowMs, ct)));
    }
}
