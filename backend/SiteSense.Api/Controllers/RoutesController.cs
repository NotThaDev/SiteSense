using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SiteSense.Api.Auth;
using SiteSense.Api.Dtos;
using SiteSense.Api.Services;
using SiteSense.Domain.Navigation;

namespace SiteSense.Api.Controllers;

[ApiController]
[Route("api/v1/sites/{siteId}/routes")]
[Authorize(Policy = SiteSensePolicies.CanSeeAggregates)]
public sealed class RoutesController(RouteService routes) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<RouteResponse>(200, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<IActionResult> Post(string siteId, RouteRequest request, CancellationToken ct)
    {
        var outcome = await routes.FindAsync(User.TenantId(), siteId, (request.From.Lat, request.From.Lon), (request.To.Lat, request.To.Lon), ct);
        if (outcome is null) return Problem(statusCode: 404, title: "Site or map not found");

        if (outcome.Status != RouteStatus.Ok)
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, 422, "No route", detail: outcome.Detail);
            problem.Extensions["reason"] = outcome.Status == RouteStatus.StartTooFar ? "START_TOO_FAR" : "UNREACHABLE";
            return new ObjectResult(problem) { StatusCode = 422, ContentTypes = { "application/problem+json" } };
        }

        return Ok(new RouteResponse(new LineStringGeometry("LineString", outcome.Coordinates),
            Math.Round(outcome.DistanceM, 1), Math.Round(outcome.DurationS, 1)));
    }
}
