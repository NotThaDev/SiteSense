using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites")]
public sealed class SitesController : ControllerBase
{
    // Same siteId again = update of that site, with If-Match (Q43). A stale or missing If-Match ends as 412 in the exception handler.
    [HttpPost]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [ProducesResponseType<CreateSiteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<CreateSiteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Create(SiteRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var site = request.ToSite(User.TenantId());
        // Missing or unreadable If-Match counts as version 0: a create, or 412 when the site exists (Q45).
        site.Version = long.TryParse(ifMatch?.Trim().Trim('"'), out var v) ? v : 0;
        var saved = await Site.SaveAsync(User.TenantId(), site, ct);
        Response.Headers.ETag = $"\"{saved.Version}\"";
        return saved.Version == 1
            ? CreatedAtAction(nameof(Get), new { siteId = saved.Id }, new CreateSiteResponse(saved.Id))
            : Ok(new CreateSiteResponse(saved.Id));
    }

    [HttpGet("{siteId}")]
    [Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
    [ProducesResponseType<SiteResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string siteId, CancellationToken ct)
    {
        var site = await Site.GetAsync(User.TenantId(), siteId, ct);
        if (site is null) return NotFound();
        Response.Headers.ETag = $"\"{site.Version}\"";
        return Ok(SiteResponse.From(site));
    }
}
