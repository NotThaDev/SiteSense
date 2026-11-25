using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api.Auth;

// Test-only: the pattern real endpoints follow — tenant from the token, passed to the store (IDN-02).
[ApiController]
[Route("probe/tenant-docs")]
public sealed class TenantProbeController : ControllerBase
{
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var doc = await SiteSenseContext.Store.GetAsync<TestDoc>(User.TenantId(), id, ct);
        return doc is null ? NotFound() : Ok(doc);
    }
}
