using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api.Auth;

// Test-only: one action per named policy; each reads a document of the caller's tenant (IDN-02).
[ApiController]
[Route("probe/policy")]
public sealed class PolicyProbeController : ControllerBase
{
    [HttpGet("manage/{id}")]
    [Authorize(Policy = SiteSensePolicies.CanManage)]
    public Task<IActionResult> Manage(string id, CancellationToken ct) => Read(id, ct);

    [HttpGet("individuals/{id}")]
    [Authorize(Policy = SiteSensePolicies.CanSeeIndividuals)]
    public Task<IActionResult> Individuals(string id, CancellationToken ct) => Read(id, ct);

    [HttpGet("aggregates/{id}")]
    [Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
    public Task<IActionResult> Aggregates(string id, CancellationToken ct) => Read(id, ct);

    [HttpGet("positions/{id}")]
    [Authorize(Policy = SiteSensePolicies.CanWritePositions)]
    public Task<IActionResult> Positions(string id, CancellationToken ct) => Read(id, ct);

    private async Task<IActionResult> Read(string id, CancellationToken ct)
    {
        var doc = await SiteSenseContext.Store.GetAsync<TestDoc>(User.TenantId(), id, ct);
        return doc is null ? NotFound() : Ok(doc);
    }
}
