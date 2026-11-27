using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Realtime;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(EventPublisher publisher, IAuthorizationService auth) : ControllerBase
{
    // Catch-up after a reconnect: one window of up to 1,000 seqs, filtered by the caller's audience (Q35).
    [HttpGet]
    [Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
    [ProducesResponseType<EventsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get([FromQuery] long after = 0, CancellationToken ct = default)
    {
        if (after < 0)
        {
            ModelState.AddModelError(nameof(after), "after must be 0 or greater.");
            return ValidationProblem(ModelState);
        }
        var passed = await EventAudience.PassedPoliciesAsync(auth, User);
        var page = await publisher.ReadAfterAsync(User.TenantId(), after, passed, ct);
        var items = page.Items.Select(e => new EventItem(e.Seq, e.Type, e.SiteId, e.AtMs, e.Data)).ToList();
        return Ok(new EventsResponse(items, page.LastSeq, page.HasMore));
    }
}

public sealed record EventsResponse(IReadOnlyList<EventItem> Items, long LastSeq, bool HasMore);

public sealed record EventItem(long Seq, string Type, string SiteId, long AtMs, JsonElement Data);
