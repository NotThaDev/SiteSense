using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.BackgroundJobs;
using SiteSense.Api.Dtos;
using SiteSense.Api.Helpers;

namespace SiteSense.Api.Controllers;

// Test mode only: Program.cs answers 404 on this prefix when SITESENSE_TEST_MODE is off.
[ApiController]
[Route("api/v1/test")]
[Authorize(Policy = SiteSensePolicies.TestMode)]
public sealed class TestController : ControllerBase
{
    // One global clock, frozen at nowMs until the next PUT (Q38).
    [HttpPut("clock")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public IActionResult SetClock(TestClockRequest request)
    {
        SiteSenseContext.Time = new FrozenTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(request.NowMs));
        return NoContent();
    }

    // Runs a job now for the caller's tenant (a tenant job once, a site job for every site) and answers when all are done (C-7.2c).
    [HttpPost("jobs/{jobName}/run")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunJob(string jobName, [FromServices] SiteJobRunner runner, CancellationToken ct) =>
        await runner.RunNowAsync(jobName, User.TenantId(), ct) ? NoContent() : NotFound();
}
