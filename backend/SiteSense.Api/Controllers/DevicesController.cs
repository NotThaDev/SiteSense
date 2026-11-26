using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Models;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/devices")]
[Authorize(Policy = SiteSensePolicies.CanManage)]
public sealed class DevicesController : ControllerBase
{
    // Create only: an id already registered in the tenant, on any site, is a 409 (Q13).
    [HttpPost]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(string siteId, DeviceRequest request, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        if (await Site.GetAsync(tenantId, siteId, ct) is null)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Site not found");
        var device = await Device.CreateAsync(tenantId, new Device { Id = request.DeviceId, TenantId = tenantId, SiteId = siteId }, ct);
        if (device is null)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Device already registered", detail: $"Device '{request.DeviceId}' is already registered in this tenant.");
        Response.Headers.ETag = $"\"{device.Version}\"";
        return CreatedAtAction(nameof(Get), new { siteId, deviceId = device.Id }, DeviceResponse.From(device));
    }

    // Admin only (Q85). A device registered on another site is a 404, like another tenant's.
    [HttpGet("{deviceId}")]
    [ProducesResponseType<DeviceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string siteId, string deviceId, CancellationToken ct)
    {
        var device = await Device.GetAsync(User.TenantId(), deviceId, ct);
        if (device is null || device.SiteId != siteId)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");
        Response.Headers.ETag = $"\"{device.Version}\"";
        return Ok(DeviceResponse.From(device));
    }

    // Moving a device = DELETE here, then POST on the new site. A device registered on another site is a 404.
    [HttpDelete("{deviceId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> Delete(string siteId, string deviceId, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        var device = await Device.GetAsync(tenantId, deviceId, ct);
        if (device is null || device.SiteId != siteId)
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");
        // Missing or unreadable If-Match counts as version 0, so the store answers 412 (Q45).
        var version = long.TryParse(ifMatch?.Trim().Trim('"'), out var v) ? v : 0;
        if (!await Device.DeleteAsync(tenantId, deviceId, version, ct))
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "Device not found");
        return NoContent();
    }
}
