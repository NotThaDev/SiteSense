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
    [HttpPost]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [ProducesResponseType<CreateSiteResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(SiteRequest request, CancellationToken ct)
    {
        var site = await Site.CreateAsync(User.TenantId(), request.ToSite(User.TenantId()), ct);
        if (site is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Site already exists", detail: $"Site '{request.SiteId}' already exists.");
        Response.Headers.ETag = $"\"{site.Version}\"";
        return CreatedAtAction(nameof(Get), new { siteId = site.Id }, new CreateSiteResponse(site.Id));
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
