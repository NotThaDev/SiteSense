using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;
using SiteSense.Api.Models.Map;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/map")]
public sealed class MapController : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
    [Produces("application/geo+json")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Get(string siteId, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        if (await Site.GetAsync(tenantId, siteId, ct) is null) return Problem(statusCode: 404, title: "Site not found");
        var map = await SiteMap.GetAsync(tenantId, siteId, ct);
        if (map is null) return Problem(statusCode: 404, title: "Map not found");

        Response.Headers.ETag = $"\"{map.Version}\"";
        return Content(map.ToGeoJson().ToJsonString(), "application/geo+json");
    }

    [HttpPut]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [Consumes("application/geo+json", "application/json")]
    [ProducesResponseType<MapVersionResponse>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(412)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<IActionResult> Put(string siteId, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        var connectToleranceM = await GetConnectToleranceAsync(tenantId, siteId, ct);
        if (connectToleranceM is null) return Problem(statusCode: 404, title: "Site not found");

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var geoJson = await reader.ReadToEndAsync(ct);

        // No header = replace the latest map; an unreadable header is a failed precondition, never "version 0".
        long? version = null;
        if (!string.IsNullOrWhiteSpace(ifMatch))
        {
            if (!long.TryParse(ifMatch.Trim().Trim('"'), out var v))
                return Problem(statusCode: 412, title: "Precondition failed", detail: "If-Match must be the map version from the ETag, e.g. \"3\".");
            version = v;
        }

        MapImportResult result;
        try
        {
            result = await SiteMap.ImportAsync(tenantId, siteId, geoJson, version, connectToleranceM.Value, ct);
        }
        catch (MapUploadConflictException)
        {
            return Problem(statusCode: 409, title: "Concurrent map uploads, try again");
        }

        if (result.Errors.Count > 0)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, 422, "The map is not valid");
            problem.Extensions["errors"] = result.Errors.Select(e => new { elementId = e.ElementId, reason = e.Reason }).ToList();
            return new ObjectResult(problem) { StatusCode = 422, ContentTypes = { "application/problem+json" } };
        }

        Response.Headers.ETag = $"\"{result.MapVersion}\"";
        return Ok(new MapVersionResponse(result.MapVersion!.Value));
    }

    // Null when the site does not exist in this tenant: 404, also for another tenant's site.
    private static async Task<double?> GetConnectToleranceAsync(string tenantId, string siteId, CancellationToken ct) =>
        (await Site.GetAsync(tenantId, siteId, ct))?.Navigation.ConnectToleranceM;
}
