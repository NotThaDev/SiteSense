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
    // Create only: an existing siteId is a 409; updates go through PUT. No siteId = a new server-made id (Q43).
    [HttpPost]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [ProducesResponseType<SiteIdResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(SiteRequest request, CancellationToken ct)
    {
        var siteId = request.SiteId ?? Guid.NewGuid().ToString("N");
        var site = await Site.CreateAsync(User.TenantId(), request.ToSite(User.TenantId(), siteId), ct);
        if (site is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Site already exists", detail: $"Site '{siteId}' already exists.");
        Response.Headers.ETag = $"\"{site.Version}\"";
        return CreatedAtAction(nameof(Get), new { siteId = site.Id }, new SiteIdResponse(site.Id));
    }

    [HttpPut("{siteId}")]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [ProducesResponseType<SiteIdResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Update(string siteId, SiteRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (request.SiteId is not null && request.SiteId != siteId)
        {
            ModelState.AddModelError(nameof(request.SiteId), "siteId in the body must be absent or match the URL.");
            return ValidationProblem(ModelState);
        }
        if (await Site.GetAsync(User.TenantId(), siteId, ct) is null) return NotFound();
        var site = request.ToSite(User.TenantId(), siteId);
        // Missing or unreadable If-Match counts as version 0, so the store answers 412 (Q45).
        site.Version = long.TryParse(ifMatch?.Trim().Trim('"'), out var v) ? v : 0;
        var saved = await Site.UpdateAsync(User.TenantId(), site, ct);
        Response.Headers.ETag = $"\"{saved.Version}\"";
        return Ok(new SiteIdResponse(saved.Id));
    }

    // Admin only, like the device list. Sorted by siteId (ordinal). Full scan, no paging (SS-11, postponed).
    [HttpGet]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    [ProducesResponseType<SiteListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var sites = await Site.ListAsync(User.TenantId(), ct);
        return Ok(new SiteListResponse(sites.OrderBy(s => s.Id, StringComparer.Ordinal).Select(SiteResponse.From).ToList()));
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
