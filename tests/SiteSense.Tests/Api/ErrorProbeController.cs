using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Models;

namespace SiteSense.Tests.Api;

// Test-only controller: exists in the test assembly, never in production.
[ApiController]
[Route("probe")]
public sealed class ErrorProbeController : ControllerBase
{
    public sealed record ProbeRequest(LatLon From);

    [HttpPost("echo")]
    public ActionResult<LatLon> Echo([FromBody] ProbeRequest request) => Ok(request.From);

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("probe-secret-detail");
}
