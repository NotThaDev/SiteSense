using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Helpers;
using SiteSense.Tests.Api.Store;

namespace SiteSense.Tests.Api;

// Test-only controller: the ETag / If-Match pattern real document endpoints copy (JDB-10).
[ApiController]
[Route("probe/docs")]
public sealed class ETagProbeController : ControllerBase
{
    private const string Tenant = "tenant-a";

    public sealed record DocBody(string Name);

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var doc = await SiteSenseContext.Store.GetAsync<TestDoc>(Tenant, id, ct);
        if (doc is null) return NotFound();
        Response.Headers.ETag = $"\"{doc.Version}\"";
        return Ok(new DocBody(doc.Name));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Put(string id, [FromBody] DocBody body, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (await SiteSenseContext.Store.GetAsync<TestDoc>(Tenant, id, ct) is null) return NotFound();
        // Missing or unreadable If-Match counts as version 0, so the store answers 412 (Q45).
        var version = long.TryParse(ifMatch?.Trim().Trim('"'), out var v) ? v : 0;
        var saved = await SiteSenseContext.Store.SaveAsync(Tenant, new TestDoc { Id = id, TenantId = Tenant, Version = version, Name = body.Name }, ct);
        Response.Headers.ETag = $"\"{saved.Version}\"";
        return Ok(new DocBody(saved.Name));
    }
}
