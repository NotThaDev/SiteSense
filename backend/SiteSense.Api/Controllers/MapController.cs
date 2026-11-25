using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models.Map;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/map")]
// shortcut: any authenticated user; switch to the Admin policy (SS-7) when it reaches main.
[Authorize]
public sealed class MapController : ControllerBase
{
    [HttpPut]
    [Consumes("application/geo+json", "application/json")]
    [ProducesResponseType<MapVersionResponse>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(412)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<IActionResult> Put(string siteId, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        var connectToleranceM = await GetConnectToleranceAsync(tenantId, siteId, ct);
        if (connectToleranceM is null) return Problem(statusCode: 404, title: "Site not found");

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var geoJson = await reader.ReadToEndAsync(ct);

        // Missing or unreadable If-Match counts as version 0, so the store answers 412.
        var version = long.TryParse(ifMatch?.Trim().Trim('"'), out var v) ? v : 0;
        var result = await SiteMap.ImportAsync(tenantId, siteId, geoJson, version, connectToleranceM.Value, ct);

        if (result.Errors.Count > 0)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, 422, "The map is not valid");
            problem.Extensions["errors"] = result.Errors.Select(e => new { elementId = e.ElementId, reason = e.Reason }).ToList();
            return new ObjectResult(problem) { StatusCode = 422, ContentTypes = { "application/problem+json" } };
        }

        Response.Headers.ETag = $"\"{result.MapVersion}\"";
        return Ok(new MapVersionResponse(result.MapVersion!.Value));
    }

    // Returns the site's connectToleranceM, or null when the site does not exist in this tenant (404, also across tenants).
    // Sites come with SS-14: until then this throws.
    private static Task<double?> GetConnectToleranceAsync(string tenantId, string siteId, CancellationToken ct) =>
        throw new NotImplementedException("Site lookup comes with SS-14 (sites API).");
}
