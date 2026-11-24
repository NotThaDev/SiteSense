using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace SiteSense.Tests.Api;

// Test-only controller: exists in the test assembly, never in production.
[ApiController]
[Route("probe")]
public sealed class ErrorProbeController : ControllerBase
{
    public sealed record ProbePoint([Range(-90.0, 90.0)] double Lat, [Range(-180.0, 180.0)] double Lon);

    public sealed record ProbeRequest(ProbePoint From);

    [HttpPost("echo")]
    public ActionResult<ProbePoint> Echo([FromBody] ProbeRequest request) => Ok(request.From);

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException("probe-secret-detail");
}
